import Foundation
import Observation

/// One entry of the vehicle picker (6.0).
public struct VehiclePickerEntry: Sendable, Equatable, Identifiable {
    public enum Dot: Sendable, Equatable { case none, red, amber, green }
    public var id: String
    public var name: String
    public var dot: Dot
    public var sub: String

    public init(_ v: Vehicle) {
        id = v.id
        name = v.name ?? v.id
        if let s = v.share {
            dot = s.healthy != true ? .red : (s.writable == false ? .amber : .green)
        } else {
            dot = .none
        }
        if let r = v.inventory?.recordings { sub = Format.plural(r, "recording") }
        else if v.scanning == true { sub = Strings.Shell.scanning }
        else { sub = Strings.Shell.notScanned }
    }
}

/// The window's shared state: vehicles, badges, the interface, live updates.
@MainActor
@Observable
public final class ShellModel {
    public let services: Services
    public private(set) var vehicles: [Vehicle] = []
    public private(set) var openNotifications: [AppNotification] = []
    public private(set) var jobs: [Job] = []
    public private(set) var deployment: String?
    public private(set) var appearance = Appearance(reduceEffects: false, clock24: false)
    public private(set) var settings: SettingsAnswer?
    public private(set) var packs: UIPacksAnswer?
    public private(set) var updates: UpdatesStatus?
    public private(set) var streamState: StreamState = .connecting
    /// counts up on every live event, for the live dots
    public private(set) var pulse = 0
    public var page: PageID = .home
    public var route: Route = Route(.home)
    /// the app's own /original (5.2)
    public var interfaceOverride: Bool {
        didSet { services.prefs.set(SkinPrefs.interfaceOverride, interfaceOverride ? "true" : nil) }
    }
    public var selectedVehicleID: String? {
        didSet { services.prefs.set(SkinPrefs.selectedVehicle, selectedVehicleID) }
    }
    /// live redraws wait while a dialog is open or a field is being edited
    public var busyHold = false {
        didSet { if !busyHold && heldTopics.isEmpty == false { let t = heldTopics; heldTopics = []; Task { await self.apply(topics: t) } } }
    }
    private var heldTopics = Set<String>()
    /// pages ask to be told when their topics change
    public var onPageTopics: ((Set<String>) -> Void)?

    public init(services: Services) {
        self.services = services
        selectedVehicleID = services.prefs.value(SkinPrefs.selectedVehicle)
        interfaceOverride = services.prefs.value(SkinPrefs.interfaceOverride) == "true"
    }

    // MARK: derived

    public var selectedVehicle: Vehicle? {
        vehicles.first { $0.id == selectedVehicleID } ?? vehicles.first
    }

    public var pickerEntries: [VehiclePickerEntry] { vehicles.map(VehiclePickerEntry.init) }
    public var binItems: Int { Int(vehicles.reduce(0) { $0 + ($1.binItems ?? 0) }) }
    public var runningJobs: Int { jobs.filter(\.isRunning).count }
    public var conversionRunning: Bool { jobs.contains { $0.isRunning && $0.isConversion } }
    public var openCount: Int { openNotifications.count }
    public var unreadOpenCount: Int { openNotifications.filter(\.isUnread).count }
    public var anyOpenCritical: Bool { openNotifications.contains { $0.level == "critical" } }
    public var critical: CriticalUpdate? { updates?.critical }
    public var clock24: Bool { appearance.clock24 ?? false }

    /// motion is off when the server says so or the Mac does (5.1)
    public func reduceMotion(systemReduceMotion: Bool) -> Bool { systemReduceMotion || (appearance.reduceEffects ?? false) }

    public var interface: InterfaceChoice {
        InterfaceResolver.resolve(packs, override: interfaceOverride,
                                  lastSkin: services.prefs.value(SkinPrefs.lastSkin).flatMap(SkinID.init(rawValue:)))
    }

    // MARK: loading

    /// startup (6.0): settings, then vehicles, notifications, jobs and info together
    public func start() async {
        await loadSettings()
        async let v: Void = loadVehicles()
        async let n: Void = loadNotifications()
        async let j: Void = loadJobs()
        async let i: Void = loadInfo()
        async let p: Void = loadPacks()
        async let u: Void = loadUpdates()
        _ = await (v, n, j, i, p, u)
    }

    public func loadSettings() async {
        if let s = try? await services.api.send(API.settings()) {
            settings = s
            if let a = s.appearance { appearance = a }
        }
    }

    public func loadVehicles() async {
        guard let a = try? await services.api.send(API.vehicles()) else { return }
        vehicles = a.vehicles ?? []
        // a vehicle that is gone falls back to the first one
        if selectedVehicle?.id != selectedVehicleID { selectedVehicleID = vehicles.first?.id }
    }

    public func loadNotifications() async {
        if let a = try? await services.api.send(API.notifications(openOnly: true)) { openNotifications = a.notifications ?? [] }
    }

    public func loadJobs() async {
        if let a = try? await services.api.send(API.jobs(limit: 50)) { jobs = a.jobs ?? [] }
    }

    public func loadInfo() async {
        if let a = try? await services.api.send(API.info()) { deployment = a.deployment }
    }

    public func loadPacks() async {
        if let a = try? await services.api.send(API.uiPacks()) {
            packs = a
            if case .skin(let s) = interface { services.prefs.set(SkinPrefs.lastSkin, s.rawValue) }
        }
    }

    public func loadUpdates() async {
        if let a = try? await services.api.send(API.updates()) { updates = a }
    }

    // MARK: settings the shell owns

    public func setClock24(_ on: Bool) async throws {
        appearance = try await services.api.send(API.setAppearance(Appearance(clock24: on)))
    }

    public func setReduceEffects(_ on: Bool) async throws {
        appearance = try await services.api.send(API.setAppearance(Appearance(reduceEffects: on)))
    }

    public func signOut() async {
        _ = try? await services.api.send(API.logout())
        services.api.session.clear()
    }

    // MARK: live updates

    public func streamChanged(_ s: StreamState) { streamState = s }

    /// a debounced batch of `changed` topics
    public func apply(topics: Set<String>) async {
        pulse += 1
        if busyHold { heldTopics.formUnion(topics); return }
        let reloads = TopicRouter.global(topics)
        await withTaskGroup(of: Void.self) { g in
            if reloads.contains(.vehicles) { g.addTask { await self.loadVehicles() } }
            if reloads.contains(.notifications) { g.addTask { await self.loadNotifications() } }
            if reloads.contains(.jobs) { g.addTask { await self.loadJobs() } }
            if reloads.contains(.interface) { g.addTask { await self.loadPacks() } }
        }
        if TopicRouter.pageReloads(page, topics: topics) { onPageTopics?(topics) }
    }

    /// opens a route: the page plus its parameters, mapped into this skin
    public func open(_ r: Route, skin: SkinID) {
        route = r
        page = PageList.host(r.page, in: skin)
    }
}
