import Foundation

/// The five interfaces the app draws (section 5).
public enum SkinID: String, Sendable, CaseIterable, Codable {
    case builtin, horizon, flux, aurora, daylight

    /// the shipped packs, by their server id
    public static let packs: [SkinID] = [.horizon, .flux, .aurora, .daylight]

    /// the prefix of this skin's per-Mac-user settings (UserDefaults)
    public var prefsPrefix: String { self == .builtin ? "sm" : rawValue }
}

/// What the main window shows, from GET /api/ui-packs (5.2).
public enum InterfaceChoice: Sendable, Equatable {
    case skin(SkinID)
    /// an uploaded pack (or a future shipped one with no native skin)
    case thirdParty(id: String, name: String)
}

public enum InterfaceResolver {
    /// `override` is the app's own "Use the Original Interface in This App"
    public static func resolve(_ answer: UIPacksAnswer?, override: Bool = false, lastSkin: SkinID? = nil) -> InterfaceChoice {
        if override { return .skin(.builtin) }
        guard let answer else { return .skin(lastSkin ?? .builtin) }
        guard let active = answer.active else { return .skin(.builtin) }
        let pack = answer.packs?.first { $0.id == active }
        if pack?.builtin == true, let skin = SkinID(rawValue: active), skin != .builtin {
            return .skin(skin)
        }
        return .thirdParty(id: active, name: pack?.name ?? active)
    }
}

/// Each skin's names for the pages, in its order (5.5).
public enum PageList {
    public struct Entry: Sendable, Equatable {
        public var page: PageID
        public var label: String
    }

    public static func pages(_ skin: SkinID) -> [Entry] {
        switch skin {
        case .builtin:
            return [e(.home, "Overview"), e(.nodes, "Nodes"), e(.footage, "Footage"), e(.viewer, "Viewer"), e(.encoder, "Encoder"),
                    e(.policies, "Policies"), e(.jobs, "Jobs"), e(.bin, "Recycle Bin"), e(.notifications, "Notifications"), e(.settings, "Settings")]
        case .horizon:
            return [e(.home, "Dashboard"), e(.footage, "Footage"), e(.viewer, "Viewer"), e(.encoder, "Encoder"), e(.vehicleEncoding, "Encoding per vehicle"),
                    e(.nodes, "Nodes"), e(.policies, "Policies"), e(.jobs, "Jobs"), e(.bin, "Recycle Bin"), e(.notifications, "Notifications"), e(.settings, "Settings")]
        case .flux:
            return [e(.home, "Overview"), e(.footage, "Footage"), e(.viewer, "Viewer"), e(.encoder, "Encoder"), e(.vehicleEncoding, "Encoding per vehicle"),
                    e(.nodes, "Nodes"), e(.policies, "Policies"), e(.jobs, "Jobs"), e(.bin, "Recycle Bin"), e(.notifications, "Notifications"), e(.settings, "Settings")]
        case .aurora:
            return [e(.home, "Sky"), e(.footage, "Footage"), e(.viewer, "Viewer"), e(.encoder, "Encoder"), e(.vehicleEncoding, "Per car"),
                    e(.nodes, "Nodes"), e(.policies, "Policies"), e(.jobs, "Jobs"), e(.bin, "Bin"), e(.notifications, "Alerts"), e(.settings, "Settings")]
        case .daylight:
            return [e(.home, "Today"), e(.serverLogs, "Server & logs"), e(.footage, "Footage"), e(.viewer, "Viewer"), e(.encoder, "Encoder"),
                    e(.vehicleEncoding, "Encoding per vehicle"), e(.nodes, "Helper servers"), e(.policies, "Cleanup rules"), e(.jobs, "Activity"),
                    e(.bin, "Recycle Bin"), e(.notifications, "Alerts"), e(.settings, "Settings")]
        }
    }

    private static func e(_ p: PageID, _ l: String) -> Entry { Entry(page: p, label: l) }

    /// where a page lives in a skin that has no page of its own for it
    public static func host(_ page: PageID, in skin: SkinID) -> PageID {
        if pages(skin).contains(where: { $0.page == page }) { return page }
        switch page {
        case .serverLogs: return .home
        case .vehicleEncoding: return .encoder
        default: return .home
        }
    }

    public static func label(_ page: PageID, in skin: SkinID) -> String {
        let host = host(page, in: skin)
        return pages(skin).first { $0.page == host }?.label ?? "Overview"
    }

    /// the window title in each skin's own form
    public static func windowTitle(_ page: PageID, in skin: SkinID, openAlerts: Int = 0) -> String {
        let l = label(page, in: skin)
        switch skin {
        case .builtin: return "\(l) · Sentry Six Manager"
        case .horizon: return "\(l) · Horizon · Sentry Six Manager"
        case .flux: return "\(l) · Flux · Sentry Six Manager"
        case .aurora: return "\(l) · Aurora · Sentry Six Manager"
        case .daylight: return openAlerts > 0 ? "(\(openAlerts)) \(l) · Sentry Six" : "\(l) · Sentry Six"
        }
    }
}

/// A route: a page plus its query parameters (5.3).
public struct Route: Sendable, Equatable, Hashable {
    public var page: PageID
    public var params: [String: String]

    public init(_ page: PageID, _ params: [String: String] = [:]) { self.page = page; self.params = params }

    /// "viewer?rec=X&day=2025-06-01", "settings/vehicles?add=1"
    public static func parse(_ s: String) -> Route? {
        let parts = s.split(separator: "?", maxSplits: 1, omittingEmptySubsequences: false)
        let head = parts[0].split(separator: "/")
        guard let first = head.first else { return nil }
        let names: [String: PageID] = [
            "overview": .home, "home": .home, "logs": .serverLogs, "footage": .footage, "viewer": .viewer, "encoder": .encoder,
            "vehicle-encoding": .vehicleEncoding, "nodes": .nodes, "policies": .policies, "jobs": .jobs, "bin": .bin,
            "notifications": .notifications, "settings": .settings, "vehicles": .settings,
        ]
        guard let page = names[String(first)] else { return nil }
        var params: [String: String] = [:]
        if head.count > 1 { params["tab"] = String(head[1]) }
        if first == "vehicles" { params["tab"] = "vehicles" }
        if parts.count > 1 {
            for pair in parts[1].split(separator: "&") {
                let kv = pair.split(separator: "=", maxSplits: 1, omittingEmptySubsequences: false)
                let k = String(kv[0]).removingPercentEncoding ?? String(kv[0])
                let v = kv.count > 1 ? (String(kv[1]).removingPercentEncoding ?? String(kv[1])) : ""
                params[k] = v
            }
        }
        return Route(page, params)
    }
}

/// Per-Mac-user settings, under each skin's prefix (5.1).
public protocol PrefsStore: Sendable {
    func value(_ key: String) -> String?
    func set(_ key: String, _ value: String?)
}

public final class MemoryPrefs: PrefsStore {
    private let values = Locked<[String: String]>([:])
    public init(_ initial: [String: String] = [:]) { values.set(initial) }
    public func value(_ key: String) -> String? { values.get()[key] }
    public func set(_ key: String, _ value: String?) { values.update { $0[key] = value } }
    public var all: [String: String] { values.get() }
}

public struct SkinPrefs: Sendable {
    public let skin: SkinID
    public let store: PrefsStore

    public init(skin: SkinID, store: PrefsStore) { self.skin = skin; self.store = store }

    public func key(_ name: String) -> String { "\(skin.prefsPrefix).\(name)" }
    public func string(_ name: String) -> String? { store.value(key(name)) }
    public func set(_ name: String, _ value: String?) { store.set(key(name), value) }
    public func bool(_ name: String, default d: Bool) -> Bool { string(name).map { $0 == "true" } ?? d }
    public func setBool(_ name: String, _ v: Bool) { set(name, v ? "true" : "false") }

    /// settings every skin shares (one car and the interface override)
    public static let selectedVehicle = "selectedVehicle"
    public static let interfaceOverride = "interfaceOverride"
    public static let lastSkin = "lastSkin"
}
