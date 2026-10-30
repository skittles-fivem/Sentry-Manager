import Foundation

/// The topics of `changed` on /api/events.
public enum Topic: String, Sendable, CaseIterable {
    case vehicles, inventory, bin, notifications, jobs, interface
}

/// What the app reloads for a topic (8.1).
public enum GlobalReload: String, Sendable, Hashable, CaseIterable {
    case vehicles      // /api/vehicles: picker and bin badge
    case notifications // /api/notifications?open=1
    case jobs          // /api/jobs?limit=50
    case interface     // /api/ui-packs, then the skin switches in place
}

/// The page kinds, the same in every skin (5.5).
public enum PageID: String, Sendable, Hashable, CaseIterable, Codable {
    case home, serverLogs, footage, viewer, encoder, vehicleEncoding, nodes, policies, jobs, bin, notifications, settings
}

public enum TopicRouter {
    public static func global(_ topics: Set<String>) -> Set<GlobalReload> {
        var out = Set<GlobalReload>()
        for t in topics {
            switch Topic(rawValue: t) {
            case .vehicles?, .inventory?, .bin?: out.insert(.vehicles)
            case .notifications?: out.insert(.notifications)
            case .jobs?: out.insert(.jobs)
            case .interface?: out.insert(.interface)
            case nil: break
            }
        }
        return out
    }

    /// the topics that make the showing page reload its own data
    public static func pageTopics(_ page: PageID) -> Set<Topic> {
        switch page {
        case .home, .serverLogs: return [.vehicles, .inventory, .notifications, .bin]
        case .footage, .policies: return [.inventory]
        case .jobs: return [.jobs]
        case .bin: return [.bin]
        case .notifications: return [.notifications]
        default: return []
        }
    }

    public static func pageReloads(_ page: PageID, topics: Set<String>) -> Bool {
        !pageTopics(page).isDisjoint(with: topics.compactMap(Topic.init(rawValue:)))
    }
}

/// Collects topics and lets them out once things have been quiet for the
/// debounce time (250 ms in the app, on top of the server's 400 ms).
public actor TopicDebouncer {
    private let clock: AppClock
    private let delay: Double
    private var pending = Set<String>()
    private var timer: Task<Void, Never>?
    private let continuation: AsyncStream<Set<String>>.Continuation
    public nonisolated let batches: AsyncStream<Set<String>>

    public init(clock: AppClock = SystemClock(), delay: Double = 0.25) {
        self.clock = clock
        self.delay = delay
        (batches, continuation) = AsyncStream<Set<String>>.makeStream()
    }

    public func add(_ topics: [String]) {
        pending.formUnion(topics)
        timer?.cancel()
        timer = Task {
            do { try await clock.sleep(delay) } catch { return }
            self.flush()
        }
    }

    private func flush() {
        guard !pending.isEmpty else { return }
        continuation.yield(pending)
        pending = []
    }
}

/// The `changed` event's data.
public struct ChangedEvent: Codable, Sendable, Equatable { public var topics: [String]? }

/// The converter's own stream (8.1): subscribe after every hello, and fetch
/// the current job after a reconnect so nothing missed stays missing.
public actor ConverterStream {
    public enum Update: Sendable, Equatable {
        case progress(ConverterProgressEvent)
        /// getCurrent after a reconnect, applied as a full update
        case current(ConverterSnapshot?)
        case benchmark(JSONValue)
        case state(StreamState)
        case unauthorized
    }

    public nonisolated let clientID: String
    private let client: APIClient
    private let stream: EventStream
    private var pump: Task<Void, Never>?
    private let continuation: AsyncStream<Update>.Continuation
    public nonisolated let updates: AsyncStream<Update>

    public init(client: APIClient, connector: StreamConnector, clientID: String = UUID().uuidString.lowercased(), clock: AppClock = SystemClock()) {
        self.client = client
        self.clientID = clientID
        let path = "/api/converter/events?client=" + PathEncoding.component(clientID)
        stream = EventStream(request: { client.makeRequest(method: .get, pathAndQuery: path, timeout: 24 * 3600) }, connector: connector, clock: clock)
        (updates, continuation) = AsyncStream<Update>.makeStream()
    }

    public func start() async {
        guard pump == nil else { return }
        let events = stream.events
        pump = Task { [weak self] in
            for await e in events { await self?.handle(e) }
        }
        await stream.start()
    }

    public func stop() async {
        pump?.cancel()
        pump = nil
        await stream.stop()
        _ = try? await client.invoke(API.invoke("unsubscribeProgress", clientID: clientID, as: JSONValue.self))
    }

    private func handle(_ e: StreamEvent) async {
        switch e {
        case .hello(let reconnect):
            _ = try? await client.invoke(API.invoke("subscribeProgress", clientID: clientID, as: JSONValue.self))
            if reconnect, case .value(let snap)? = try? await client.invoke(API.invoke("getCurrent", clientID: clientID, as: ConverterSnapshot.self)) {
                continuation.yield(.current(snap))
            }
        case .event(let ev):
            if ev.event == "converter:progress", let p = ev.decode(ConverterProgressEvent.self) { continuation.yield(.progress(p)) }
            else if ev.event == "converter:benchmarkProgress", let v = ev.decode(JSONValue.self) { continuation.yield(.benchmark(v)) }
        case .state(let s):
            continuation.yield(.state(s))
        case .unauthorized:
            continuation.yield(.unauthorized)
        }
    }
}
