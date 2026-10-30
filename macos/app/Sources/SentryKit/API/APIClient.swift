import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// What went wrong with a call, with the words the app shows.
public enum APIError: Error, Equatable, Sendable {
    /// the server answered with an error: its own words, shown as they are
    case http(status: Int, message: String, code: String?)
    case unreachable
    case timedOut
    /// a 401 outside /api/auth/: the session is gone
    case signedOut
    /// the password confirm was cancelled or wrong three times
    case notConfirmed
    case bodyTooLarge
    case badAnswer(String)

    public var message: String {
        switch self {
        case .http(_, let m, _): return m
        case .unreachable: return Strings.Shell.cantReach + "\n" + Strings.Shell.startFromMenuBar
        case .timedOut: return Strings.Shell.tookTooLong
        case .signedOut: return Strings.Shell.signedOut
        case .notConfirmed: return Strings.Confirm.notConfirmed
        case .bodyTooLarge: return Strings.Shell.tooLarge
        case .badAnswer: return Strings.Shell.badAnswer
        }
    }

    public var status: Int? { if case .http(let s, _, _) = self { return s }; return nil }
}

/// Asks the person for their password (the app shows the sheet; tests
/// answer from a list). `retry` is true after a wrong password. nil = Cancel.
public typealias ConfirmProvider = @Sendable (_ retry: Bool) async -> String?

/// Holds at most one password confirm at a time: requests that hit the
/// prompt together share it.
public actor ConfirmCoordinator {
    private var inFlight: Task<Void, Error>?

    public init() {}

    public func run(_ body: @escaping @Sendable () async throws -> Void) async throws {
        if let t = inFlight { return try await t.value }
        let t = Task { try await body() }
        inFlight = t
        defer { inFlight = nil }
        try await t.value
    }
}

/// The engine's JSON API over HTTPS, with the transport rules of 2.5.
public actor APIClient {
    public nonisolated let baseURL: URL
    private let transport: HTTPTransport
    public nonisolated let session: SessionStore
    private let confirms = ConfirmCoordinator()
    private var confirmProvider: ConfirmProvider?
    private var onSessionLost: (@Sendable () async -> Void)?

    public static let maxBody = 1024 * 1024
    public static let maxConverterBody = 256 * 1024

    public init(baseURL: URL, transport: HTTPTransport, session: SessionStore) {
        self.baseURL = baseURL
        self.transport = transport
        self.session = session
    }

    public func setConfirmProvider(_ p: ConfirmProvider?) { confirmProvider = p }
    public func setOnSessionLost(_ f: (@Sendable () async -> Void)?) { onSessionLost = f }

    /// The request every call, stream and media load is built from.
    public nonisolated func makeRequest(method: HTTPMethod, pathAndQuery: String, body: Data? = nil,
                                        timeout: TimeInterval = 30, headers: [String: String] = [:]) -> URLRequest {
        var req = URLRequest(url: URL(string: baseURL.absoluteString + pathAndQuery)!)
        req.httpMethod = method.rawValue
        req.timeoutInterval = timeout
        req.httpShouldHandleCookies = false
        req.cachePolicy = .reloadIgnoringLocalCacheData
        if method != .get && method != .head { req.setValue("sentry-six-manager", forHTTPHeaderField: "X-Requested-With") }
        if let body, !body.isEmpty {
            req.httpBody = body
            req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        }
        // the cookie only ever goes over HTTPS
        if isSecure, let token = session.token() { req.setValue(SessionCookie.header(token), forHTTPHeaderField: "Cookie") }
        for (k, v) in headers { req.setValue(v, forHTTPHeaderField: k) }
        return req
    }

    /// Only https://127.0.0.1 is the engine; anything else gets no request.
    public nonisolated var isSecure: Bool {
        baseURL.scheme?.lowercased() == "https" && baseURL.host == "127.0.0.1"
    }

    /// Sends a call and decodes its answer; handles 401 and the password confirm.
    public func send<R>(_ e: Endpoint<R>) async throws -> R {
        let (data, _) = try await sendRaw(e)
        if R.self == OKAnswer.self && data.isEmpty { return OKAnswer(ok: true) as! R }
        do {
            return try JSON.decode(R.self, from: data.isEmpty ? Data("null".utf8) : data)
        } catch {
            throw APIError.badAnswer("\(e.path): \(error)")
        }
    }

    /// Like send, for answers that are not JSON (downloads, thumbnails).
    public func sendRaw<R>(_ e: Endpoint<R>) async throws -> (Data, HTTPURLResponse) {
        let limit = e.path.hasPrefix("/api/converter/") ? Self.maxConverterBody : Self.maxBody
        if let b = e.body, b.count > limit, e.path != "/api/ui-packs/upload" { throw APIError.bodyTooLarge }
        return try await perform(e, confirmed: false)
    }

    /// An /api/converter/invoke/* call as a typed result.
    public func invoke<V>(_ e: Endpoint<ConverterEnvelope<V>>) async throws -> ConverterResult<V> {
        let env = try await send(e)
        if env.ok { return .value(env.value) }
        return .failure(env.error ?? ConverterError(code: "INTERNAL_ERROR"))
    }

    private func perform<R>(_ e: Endpoint<R>, confirmed: Bool) async throws -> (Data, HTTPURLResponse) {
        // passwords, tokens and the cookie never go over plain HTTP
        guard isSecure else { throw APIError.unreachable }
        let req = makeRequest(method: e.method, pathAndQuery: e.pathAndQuery, body: e.body, timeout: e.timeout.seconds, headers: e.headers)
        let data: Data
        let res: HTTPURLResponse
        do {
            (data, res) = try await transport.send(req)
        } catch let err as URLError where err.code == .timedOut {
            throw APIError.timedOut
        } catch is CancellationError {
            throw CancellationError()
        } catch {
            throw APIError.unreachable
        }
        // a sign-in, setup or password change hands out a new cookie; logout clears it
        switch SessionCookie.change(fromSetCookie: res.value(forHTTPHeaderField: "Set-Cookie")) {
        case .set(let t)?: session.setToken(t)
        case .cleared?: session.clear()
        case nil: break
        }
        if (200..<300).contains(res.statusCode) || res.statusCode == 304 { return (data, res) }
        let env = try? JSON.decode(ErrorEnvelope.self, from: data)
        if res.statusCode == 401 && !e.path.hasPrefix("/api/auth/") {
            session.clear()
            if let f = onSessionLost { await f() }
            throw APIError.signedOut
        }
        if res.statusCode == 403 && env?.code == "CONFIRM_PASSWORD" && !confirmed {
            try await confirmPassword()
            return try await perform(e, confirmed: true)
        }
        throw APIError.http(status: res.statusCode, message: env?.error ?? HTTPStatusText.text(res.statusCode), code: env?.code)
    }

    /// Up to three tries; only "not right" asks again (2.5).
    private func confirmPassword() async throws {
        guard let provider = confirmProvider else { throw APIError.notConfirmed }
        try await confirms.run { [self] in
            for attempt in 0..<3 {
                guard let password = await provider(attempt > 0) else { throw APIError.notConfirmed }
                do {
                    _ = try await self.send(API.confirm(password: password))
                    return
                } catch APIError.http(let status, let message, _) where status == 401 && message.contains("not right") {
                    continue
                }
            }
            throw APIError.notConfirmed
        }
    }
}

/// The status line's words, for an error answer without a body.
public enum HTTPStatusText {
    public static func text(_ code: Int) -> String {
        switch code {
        case 400: return "Bad Request"
        case 401: return "Unauthorized"
        case 403: return "Forbidden"
        case 404: return "Not Found"
        case 405: return "Method Not Allowed"
        case 409: return "Conflict"
        case 413: return "Payload Too Large"
        case 416: return "Range Not Satisfiable"
        case 421: return "Misdirected Request"
        case 429: return "Too Many Requests"
        case 500: return "Internal Server Error"
        case 502: return "Bad Gateway"
        case 503: return "Service Unavailable"
        case 504: return "Gateway Timeout"
        default: return "HTTP \(code)"
        }
    }
}
