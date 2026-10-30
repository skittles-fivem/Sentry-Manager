import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// Sends one request. The app's transport is a pinned URLSession; tests use
/// a fake that records requests and answers from a script.
public protocol HTTPTransport: Sendable {
    func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse)
}

/// URLSession with its own cookie handling turned off: the client reads
/// and sends the session cookie itself (2.5).
public final class URLSessionTransport: HTTPTransport, @unchecked Sendable {
    public let session: URLSession

    /// `delegate` is the app's pinning delegate (PinnedTrust); nil only in tests.
    public init(delegate: URLSessionDelegate? = nil, maxConnectionsPerHost: Int = 6) {
        let config = URLSessionConfiguration.ephemeral
        config.httpCookieStorage = nil
        config.httpShouldSetCookies = false
        config.httpCookieAcceptPolicy = .never
        config.urlCache = nil
        config.requestCachePolicy = .reloadIgnoringLocalCacheData
        config.httpMaximumConnectionsPerHost = maxConnectionsPerHost
        session = URLSession(configuration: config, delegate: delegate, delegateQueue: nil)
    }

    public func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        try await withCheckedThrowingContinuation { cont in
            let task = session.dataTask(with: request) { data, response, error in
                if let error { cont.resume(throwing: error); return }
                guard let http = response as? HTTPURLResponse else { cont.resume(throwing: URLError(.badServerResponse)); return }
                cont.resume(returning: (data ?? Data(), http))
            }
            task.resume()
        }
    }
}

/// A value behind a lock, for the few shared mutable bits outside actors.
public final class Locked<Value>: @unchecked Sendable {
    private var value: Value
    private let lock = NSLock()
    public init(_ value: Value) { self.value = value }
    public func get() -> Value { lock.lock(); defer { lock.unlock() }; return value }
    public func set(_ v: Value) { lock.lock(); value = v; lock.unlock() }
    @discardableResult
    public func update<R>(_ body: (inout Value) -> R) -> R { lock.lock(); defer { lock.unlock() }; return body(&value) }
}
