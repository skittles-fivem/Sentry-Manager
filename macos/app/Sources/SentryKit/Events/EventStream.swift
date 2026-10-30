import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// Opens a streaming request and reports what arrives. The app's connector
/// is a URLSessionDataDelegate on the pinned session (URLSession.bytes does
/// not exist on Linux); tests replay canned streams.
public protocol StreamConnector: Sendable {
    func open(_ request: URLRequest, handler: StreamHandler) -> StreamConnection
}

public protocol StreamConnection: Sendable {
    func cancel()
}

/// Callbacks of one connection; called in order, `complete` once.
public struct StreamHandler: Sendable {
    public var response: @Sendable (Int) -> Void
    public var data: @Sendable (Data) -> Void
    public var complete: @Sendable (Error?) -> Void
}

/// Backoff from 1 s, doubling to 30 s, reset after a good open (8.2).
public struct Backoff: Sendable, Equatable {
    public var first: Double = 1
    public var maximum: Double = 30
    private var current: Double?

    public init(first: Double = 1, maximum: Double = 30) { self.first = first; self.maximum = maximum }

    public mutating func next() -> Double {
        let v = current.map { min(maximum, $0 * 2) } ?? first
        current = v
        return v
    }

    public mutating func reset() { current = nil }
}

public enum StreamState: Sendable, Equatable { case connecting, connected, reconnecting, stopped }

/// What an EventStream reports.
public enum StreamEvent: Sendable, Equatable {
    case state(StreamState)
    /// the server's `hello`; `reconnect` is true for every hello but the first
    case hello(reconnect: Bool)
    case event(SSEEvent)
    /// a 401: the session is gone (2.5)
    case unauthorized
}

/// A server-sent event stream that keeps itself open: reconnects with
/// backoff, closes a stream that is silent for 45 s, stops on 401.
public actor EventStream {
    private let makeRequest: @Sendable () -> URLRequest
    private let connector: StreamConnector
    private let clock: AppClock
    private let deadAfter: Double
    private var backoff = Backoff()
    private var runner: Task<Void, Never>?
    private var connection: StreamConnection?
    private nonisolated let hellos = Locked(0)
    private nonisolated let continuation: AsyncStream<StreamEvent>.Continuation
    public nonisolated let events: AsyncStream<StreamEvent>

    public init(request: @escaping @Sendable () -> URLRequest, connector: StreamConnector, clock: AppClock = SystemClock(), deadAfter: Double = 45) {
        makeRequest = request
        self.connector = connector
        self.clock = clock
        self.deadAfter = deadAfter
        (events, continuation) = AsyncStream<StreamEvent>.makeStream(bufferingPolicy: .unbounded)
    }

    public func start() {
        guard runner == nil else { return }
        runner = Task { await self.loop() }
    }

    public func stop() {
        runner?.cancel()
        runner = nil
        connection?.cancel()
        connection = nil
        continuation.yield(.state(.stopped))
    }

    /// after sleep and wake: drop the connection now and open a new one
    public func reopen() {
        connection?.cancel()
    }

    private enum Ended { case closed(opened: Bool), unauthorized }

    private func loop() async {
        var first = true
        while !Task.isCancelled {
            continuation.yield(.state(first ? .connecting : .reconnecting))
            first = false
            let ended = await connectOnce()
            if Task.isCancelled { break }
            if case .unauthorized = ended {
                continuation.yield(.unauthorized)
                continuation.yield(.state(.stopped))
                runner = nil
                return
            }
            if case .closed(true) = ended { backoff.reset() }
            let wait = backoff.next()
            do { try await clock.sleep(wait) } catch { break }
        }
    }

    private func connectOnce() async -> Ended {
        let box = ConnectionBox()
        let lastByte = Locked(clock.now())
        let clock = self.clock
        let deadAfter = self.deadAfter
        return await withCheckedContinuation { (cont: CheckedContinuation<Ended, Never>) in
            let finished = Locked(false)
            let status = Locked(0)
            let parser = Locked(SSEParser())
            let watchdog = Locked<Task<Void, Never>?>(nil)
            let finish: @Sendable (Ended) -> Void = { e in
                let first = finished.update { f -> Bool in let was = f; f = true; return !was }
                guard first else { return }
                watchdog.get()?.cancel()
                cont.resume(returning: e)
            }
            let handler = StreamHandler(
                response: { [weak self] code in
                    status.set(code)
                    lastByte.set(clock.now())
                    if code == 200 { self?.continuation.yield(.state(.connected)) }
                },
                data: { [weak self] chunk in
                    guard status.get() == 200 else { return }
                    lastByte.set(clock.now())
                    let events = parser.update { $0.feed(chunk) }
                    for e in events { self?.handle(e) }
                },
                complete: { _ in
                    let code = status.get()
                    finish(code == 401 ? .unauthorized : .closed(opened: code == 200))
                }
            )
            let conn = connector.open(makeRequest(), handler: handler)
            box.set(conn)
            setConnection(conn)
            // a stream with no bytes for deadAfter seconds is dead: close it
            watchdog.set(Task {
                while !Task.isCancelled {
                    let idle = clock.now() - lastByte.get()
                    let wait = max(0.001, deadAfter - idle)
                    do { try await clock.sleep(wait) } catch { return }
                    if clock.now() - lastByte.get() >= deadAfter - 1e-9 {
                        box.get()?.cancel()
                        finish(.closed(opened: status.get() == 200))
                        return
                    }
                }
            })
            if finished.get() { watchdog.get()?.cancel() }
        }
    }

    private func setConnection(_ c: StreamConnection) { connection = c }

    // events go out in the order they arrived, straight from the callback
    private nonisolated func handle(_ e: SSEEvent) {
        if e.event == "hello" {
            let n = hellos.update { h -> Int in h += 1; return h }
            continuation.yield(.hello(reconnect: n > 1))
        } else {
            continuation.yield(.event(e))
        }
    }
}

final class ConnectionBox: @unchecked Sendable {
    private let value = Locked<StreamConnection?>(nil)
    func set(_ c: StreamConnection) { value.set(c) }
    func get() -> StreamConnection? { value.get() }
}

/// The real connector: a URLSessionDataDelegate on the given configuration.
/// The app passes its pinning delegate as `trust`.
public final class URLSessionStreamConnector: NSObject, StreamConnector, URLSessionDataDelegate, @unchecked Sendable {
    private var session: URLSession!
    private let handlers = Locked<[Int: StreamHandler]>([:])
    private let trust: (@Sendable (URLAuthenticationChallenge) -> (URLSession.AuthChallengeDisposition, URLCredential?))?

    public init(trust: (@Sendable (URLAuthenticationChallenge) -> (URLSession.AuthChallengeDisposition, URLCredential?))? = nil) {
        self.trust = trust
        super.init()
        let config = URLSessionConfiguration.ephemeral
        config.httpCookieStorage = nil
        config.httpShouldSetCookies = false
        config.urlCache = nil
        config.timeoutIntervalForRequest = 24 * 3600
        config.timeoutIntervalForResource = 7 * 24 * 3600
        session = URLSession(configuration: config, delegate: self, delegateQueue: nil)
    }

    struct Conn: StreamConnection {
        let task: URLSessionDataTask
        func cancel() { task.cancel() }
    }

    public func open(_ request: URLRequest, handler: StreamHandler) -> StreamConnection {
        var req = request
        req.setValue("text/event-stream", forHTTPHeaderField: "Accept")
        let task = session.dataTask(with: req)
        handlers.update { $0[task.taskIdentifier] = handler }
        task.resume()
        return Conn(task: task)
    }

    public func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse,
                           completionHandler: @escaping (URLSession.ResponseDisposition) -> Void) {
        let code = (response as? HTTPURLResponse)?.statusCode ?? 0
        handlers.get()[dataTask.taskIdentifier]?.response(code)
        completionHandler(code == 200 ? .allow : .cancel)
    }

    public func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
        handlers.get()[dataTask.taskIdentifier]?.data(data)
    }

    public func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        let h = handlers.update { $0.removeValue(forKey: task.taskIdentifier) }
        h?.complete(error)
    }

    public func urlSession(_ session: URLSession, didReceive challenge: URLAuthenticationChallenge,
                           completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void) {
        if let trust { let (d, c) = trust(challenge); completionHandler(d, c) } else { completionHandler(.performDefaultHandling, nil) }
    }
}
