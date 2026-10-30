import Foundation

/// Where the session cookie's value lives. The app keeps it in the Keychain
/// (service "Sentry Six Manager session", account = the engine's CA
/// fingerprint); tests use MemorySessionStore.
public protocol SessionStore: Sendable {
    func token() -> String?
    func setToken(_ token: String)
    func clear()
}

public final class MemorySessionStore: SessionStore {
    private let value = Locked<String?>(nil)
    public init(token: String? = nil) { value.set(token) }
    public func token() -> String? { value.get() }
    public func setToken(_ token: String) { value.set(token) }
    public func clear() { value.set(nil) }
}

/// The session cookie: its name and how to read it from Set-Cookie.
public enum SessionCookie {
    public static let name = "__Host-sm_session"
    public static let keychainService = "Sentry Six Manager session"

    public enum Change: Equatable, Sendable {
        case set(String)
        case cleared
    }

    /// What a response's Set-Cookie headers do to the session, if anything.
    /// Several cookies may come joined by ", " (Linux Foundation does that).
    public static func change(fromSetCookie header: String?) -> Change? {
        guard let header, let range = header.range(of: name + "=") else { return nil }
        let rest = header[range.upperBound...]
        let value = String(rest.prefix { $0 != ";" && $0 != "," }).trimmingCharacters(in: .whitespaces)
        let attrs = rest.drop { $0 != ";" }.prefix { $0 != "," }.lowercased()
        if value.isEmpty || attrs.contains("max-age=0") { return .cleared }
        return .set(value)
    }

    public static func header(_ token: String) -> String { "\(name)=\(token)" }
}
