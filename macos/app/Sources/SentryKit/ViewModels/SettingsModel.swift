import Foundation
import Observation

/// The source fields as the web's form sends them (port as typed, or null).
public struct SourceFormBody: Encodable, Sendable, Equatable {
    public var type: String
    public var path: String?
    public var share: String?
    public var username: String?
    public var password: String?
    public var domain: String?
    public var version: String?
    public var encrypt: Bool?
    public var port: String?

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(type, forKey: .type)
        if type == "path" { try c.encode(path, forKey: .path); return }
        try c.encode(share, forKey: .share)
        try c.encode(username, forKey: .username)
        try c.encodeIfPresent(password, forKey: .password)
        try c.encode(domain, forKey: .domain)
        try c.encode(version, forKey: .version)
        try c.encode(encrypt, forKey: .encrypt)
        try c.encode(port, forKey: .port)
    }

    enum CodingKeys: String, CodingKey { case type, path, share, username, password, domain, version, encrypt, port }
}

struct VehicleFormBody: Encodable { var name: String; var timezone: String; var source: SourceFormBody? }
struct TestSMBFormBody: Encodable {
    var vehicleId: String?
    var source: SourceFormBody
    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(vehicleId, forKey: .vehicleId)
        try c.encode(source, forKey: .source)
    }
    enum CodingKeys: String, CodingKey { case vehicleId, source }
}

/// Add or edit a vehicle (Settings › Vehicles): the fields, Test connection,
/// and Save that sends the source only when it changed.
@MainActor
@Observable
public final class VehicleFormModel {
    private let services: Services
    /// nil = a new vehicle
    public let editing: Vehicle?
    public var name: String
    public var timezone: String
    /// 'smb' or 'path'
    public var type: String
    public var path: String
    public var share: String
    public var username: String
    public var password = ""
    public var domain: String
    public var version: String
    public var encrypt: Bool
    public var port: String
    public private(set) var testText: String?
    public private(set) var testOK: Bool?
    public private(set) var testing = false

    public init(services: Services, editing: Vehicle?, defaultSMBVersion: String?, defaultTimezone: String) {
        self.services = services
        self.editing = editing
        let s = editing?.source
        name = editing?.name ?? ""
        timezone = editing?.timezone ?? defaultTimezone
        type = s?.type ?? "smb"
        path = s?.path ?? ""
        share = s?.share ?? ""
        username = s?.username ?? ""
        domain = s?.domain ?? ""
        version = s?.version ?? defaultSMBVersion ?? "3.1.1"
        encrypt = s == nil ? true : (s?.encrypt != false)
        port = s?.port.map { JS.string($0) } ?? ""
    }

    public var sourceBody: SourceFormBody {
        type == "path" ? SourceFormBody(type: "path", path: path)
            : SourceFormBody(type: "smb", share: share, username: username, password: password.isEmpty ? nil : password,
                             domain: domain, version: version, encrypt: encrypt, port: port.isEmpty ? nil : port)
    }

    /// a rename alone needs no password: the source goes only when it changed
    public var sourceUnchanged: Bool {
        guard let was = editing?.source else { return false }
        guard type == was.type else { return false }
        if type == "path" { return path == was.path }
        let b = sourceBody
        let same = b.share ?? "" == was.share ?? "" && b.username ?? "" == was.username ?? "" &&
            b.domain ?? "" == was.domain ?? "" && b.version ?? "" == was.version ?? ""
        return same && b.encrypt == (was.encrypt != false) && (b.port ?? "") == (was.port.map { JS.string($0) } ?? "") && b.password == nil
    }

    public func testConnection() async {
        testing = true
        defer { testing = false }
        testText = "Connecting to the NAS…"
        testOK = nil
        do {
            let body = TestSMBFormBody(vehicleId: editing?.id, source: sourceBody)
            let r = try await services.api.send(Endpoint<TestSMBAnswer>(.post, "/api/vehicles/test-smb", json: body, timeout: .custom(60)))
            testOK = r.ok == true
            testText = Self.testResultText(r)
        } catch {
            testOK = false
            testText = errorText(error)
        }
    }

    /// the words under Test connection, as the web builds them
    public static func testResultText(_ r: TestSMBAnswer) -> String {
        if r.ok == true {
            let footage = r.footage?.string
            let found = footage != nil && !(footage!.isEmpty)
                ? "Tesla footage found\(footage == "." ? "" : " in \(footage!)")."
                : "No TeslaCam folder found yet (empty share, or the footage is deeper than 3 folders)."
            let write = r.writable == true ? "" : (r.writeProblem ?? "Read-only for this account: the Recycle Bin, Protect and replacing originals need write access.")
            return "Connected: the user name and password work. \(found) \(write)".trimmingCharacters(in: .whitespaces)
        }
        return "\(r.stage == "details" ? "" : "Could not connect: ")\(r.error ?? "")\(r.hint.map { " \($0)" } ?? "")"
    }

    /// Save: answers the saved vehicle; the toast is "Saved. Connecting to the footage…"
    public func save() async throws -> Vehicle {
        let body = VehicleFormBody(name: name, timezone: timezone, source: editing != nil && sourceUnchanged ? nil : sourceBody)
        if let v = editing {
            return try await services.api.send(Endpoint<Vehicle>(.put, "/api/vehicles/\(PathEncoding.component(v.id))", json: body))
        }
        return try await services.api.send(Endpoint<Vehicle>(.post, "/api/vehicles", json: body))
    }

    public static let savedToast = "Saved. Connecting to the footage…"

    public func remove() async throws {
        guard let v = editing else { return }
        _ = try await services.api.send(API.deleteVehicle(v.id))
    }
}

/// One electricity rate row as typed.
public struct RateDraft: Sendable, Equatable {
    public var label: String
    public var price: String
    public var from: String
    public var to: String
    public var days: Set<Int>
    public init(label: String = "", price: String = "", from: String = "", to: String = "", days: Set<Int> = []) {
        self.label = label; self.price = price; self.from = from; self.to = to; self.days = days
    }
}

/// Settings: the sections the built-in skin shows (6.12), minus the vehicle
/// form (VehicleFormModel) and This Mac (the service unit).
@MainActor
@Observable
public final class SettingsModel {
    private let services: Services
    public private(set) var system: SystemAnswer?
    public private(set) var security: SecurityAnswer?
    public private(set) var settings: SettingsAnswer?
    public private(set) var packs: UIPacksAnswer?
    public private(set) var updates: UpdatesStatus?
    public var hostnames = ""
    public var currency = "$"
    public var mode = "flat"
    public var pricePerKwh = ""
    public var defaultPrice = ""
    public var rates: [RateDraft] = []

    public init(services: Services) { self.services = services }

    public func load() async {
        async let sy = try? services.api.send(API.system())
        async let se = try? services.api.send(API.security())
        async let st = try? services.api.send(API.settings())
        async let pk = try? services.api.send(API.uiPacks())
        async let up = try? services.api.send(API.updates())
        system = await sy
        security = await se
        settings = await st
        packs = await pk
        updates = await up
        hostnames = (security?.tls?.editable ?? []).joined(separator: ", ")
        if let p = settings?.power {
            currency = p.currency ?? "$"
            mode = p.mode ?? "flat"
            pricePerKwh = p.pricePerKwh.map { JS.string($0) } ?? ""
            defaultPrice = p.defaultPrice.map { JS.string($0) } ?? ""
            rates = (p.rates ?? []).map { RateDraft(label: $0.label ?? "", price: $0.price.map { JS.string($0) } ?? "", from: $0.from ?? "", to: $0.to ?? "",
                                                    days: Set(($0.days ?? []).map { Int($0) })) }
        }
    }

    /// the power body, or the web's message when a price is not a number
    public func powerBody() -> Result<PowerSettings, PowerError> {
        func num(_ s: String) -> Double?? {
            let t = s.trimmingCharacters(in: .whitespaces)
            if t.isEmpty { return .some(nil) }
            return Double(t).map { .some($0) } ?? nil
        }
        guard let flat = num(pricePerKwh), let other = num(defaultPrice) else { return .failure(PowerError()) }
        var out: [PowerRate] = []
        for r in rates {
            guard let p = num(r.price) else { return .failure(PowerError()) }
            out.append(PowerRate(from: r.from, to: r.to, price: p, days: r.days.sorted().map(Double.init), label: r.label))
        }
        return .success(PowerSettings(currency: currency, mode: mode, pricePerKwh: flat, defaultPrice: other, rates: out))
    }

    public struct PowerError: Error, Equatable { public let message = "Enter prices as numbers, like 0.15." }

    public func savePower() async throws {
        switch powerBody() {
        case .failure(let e): throw APIError.http(status: 400, message: e.message, code: nil)
        case .success(let b): _ = try await services.api.send(API.setPower(b)); await load()
        }
    }

    /// the names as typed: commas or spaces between them
    public var hostnameList: [String] {
        hostnames.split { $0 == "," || $0 == " " || $0 == "\n" }.map(String.init).filter { !$0.isEmpty }
    }

    public func saveHostnames() async throws -> HostnamesAnswer {
        let a = try await services.api.send(API.saveHostnames(hostnameList))
        await load()
        return a
    }

    public func changePassword(current: String, next: String, confirm: String) async throws {
        if next != confirm { throw APIError.http(status: 400, message: Strings.SignIn.mismatch, code: nil) }
        _ = try await services.api.send(API.changePassword(current: current, next: next))
    }

    public func usePack(_ id: String?) async throws { _ = try await services.api.send(API.setActivePack(id)); await load() }
    public func deletePack(_ id: String) async throws { _ = try await services.api.send(API.deletePack(id)); await load() }
    public func uploadPack(zip: Data, fileName: String) async throws -> UIPack? {
        let a = try await services.api.send(API.uploadPack(zip: zip, fileName: fileName))
        await load()
        return a.pack
    }

    public func checkUpdates() async throws { updates = try await services.api.send(API.checkUpdates()) }

    public func reconnect(_ vehicleID: String) async throws -> ShareStatus? { try await services.api.send(API.reconnect(vehicleID)).share }

    /// same-Mac sign-in (2.6), only where the server has the setting
    public var localSignIn: LocalSignInSetting? { settings?.localSignIn }

    public func setLocalSignIn(enabled: Bool, user: String?) async throws {
        _ = try await services.api.send(API.setLocalSignIn(enabled: enabled, user: user))
        await load()
    }
}
