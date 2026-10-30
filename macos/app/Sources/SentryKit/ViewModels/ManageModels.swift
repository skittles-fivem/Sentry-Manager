import Foundation
import Observation

/// Jobs: this vehicle or every vehicle, a status filter, search, a 1 s tick
/// for running durations, and Clear with its preview.
@MainActor
@Observable
public final class JobsModel {
    private let services: Services
    public private(set) var jobs: [Job] = []
    public private(set) var state: LoadState = .idle
    /// nil = every vehicle
    public var vehicleID: String?
    /// nil = all, else 'running', 'done', 'failed', 'cancelled'
    public var status: String?
    public var search = ""
    public private(set) var now: Double
    private let ticker: Poller

    public init(services: Services) {
        self.services = services
        now = services.nowMs()
        ticker = Poller(clock: services.clock)
    }

    public func load(limit: Int = 200) async {
        state = .loading
        do {
            jobs = try await services.api.send(API.jobs(vehicle: vehicleID, limit: limit)).jobs ?? []
            state = .loaded
        } catch { state = .failed(errorText(error)) }
    }

    public var shown: [Job] {
        let needle = search.lowercased()
        return jobs.filter { j in
            (status == nil || j.status == status) &&
                (needle.isEmpty || (j.title ?? "").lowercased().contains(needle) || (j.detail ?? "").lowercased().contains(needle))
        }
    }

    /// "42 s", "12 m 5 s": finished jobs from their times, running ones up to now
    public func duration(_ j: Job) -> String? {
        guard let s = j.startedAt else { return nil }
        return Format.took((j.finishedAt ?? now) - s)
    }

    public func appear() { ticker.start(every: 1) { [weak self] in guard let self else { return }; self.now = self.services.nowMs() } }
    public func disappear() { ticker.stop() }

    /// nil when there is nothing to clear
    public func clearPreview(vehicleName: String?, manyVehicles: Bool) async throws -> ConfirmText? {
        let p = try await services.api.send(API.clearJobs(vehicle: vehicleID, preview: true))
        guard let n = p.jobs, n > 0 else { return nil }
        return Confirms.clearJobs(count: n, vehicleName: vehicleName, manyVehicles: manyVehicles)
    }

    public func clear() async throws -> Int {
        let a = try await services.api.send(API.clearJobs(vehicle: vehicleID, preview: false))
        await load()
        return Int(a.cleared ?? 0)
    }
}

/// Policies: every vehicle's policy at once.
@MainActor
@Observable
public final class PoliciesModel {
    public static let categories = ["Recent", "Sentry", "Saved", "Other"]
    private let services: Services
    public private(set) var policies: [String: PolicyAnswer] = [:]
    public private(set) var state: LoadState = .idle

    public init(services: Services) { self.services = services }

    public func load(vehicles: [Vehicle]) async {
        state = .loading
        var out: [String: PolicyAnswer] = [:]
        do {
            for v in vehicles { out[v.id] = try await services.api.send(API.policy(v.id)) }
            policies = out
            state = .loaded
        } catch { state = .failed(errorText(error)) }
    }

    /// "30" -> 30, "" -> nil (keep forever)
    public static func keepDays(from text: String) -> Double?? {
        let t = text.trimmingCharacters(in: .whitespaces)
        if t.isEmpty { return .some(nil) }
        guard let n = Double(t), n.rounded() == n else { return nil }
        return .some(n)
    }

    public func save(_ vehicleID: String, _ body: PolicyBody) async throws {
        policies[vehicleID] = try await services.api.send(API.setPolicy(vehicleID, body))
    }

    public func runConfirm(_ vehicleID: String) -> ConfirmText? {
        guard let p = policies[vehicleID]?.preview else { return nil }
        return Confirms.runPolicy(p)
    }

    public func run(_ vehicleID: String) async throws -> TrashAnswer {
        let a = try await services.api.send(API.runPolicy(vehicleID))
        policies[vehicleID] = try? await services.api.send(API.policy(vehicleID))
        return a
    }
}

/// The Recycle Bin: scope, a filter, multi-select restore, delete forever, empty.
@MainActor
@Observable
public final class BinModel {
    private let services: Services
    public private(set) var items: [BinItem] = []
    public private(set) var state: LoadState = .idle
    public var vehicleID: String?
    public var filter = ""
    public var selected = Set<String>()

    public init(services: Services) { self.services = services }

    public func load() async {
        state = .loading
        do {
            items = try await services.api.send(API.bin(vehicle: vehicleID)).items ?? []
            selected = selected.intersection(items.map(\.id))
            state = .loaded
        } catch { state = .failed(errorText(error)) }
    }

    public var shown: [BinItem] {
        let n = filter.lowercased()
        return n.isEmpty ? items : items.filter { ($0.label ?? "").lowercased().contains(n) || ($0.category ?? "").lowercased().contains(n) }
    }

    public var totalBytes: Double { items.reduce(0) { $0 + ($1.bytes ?? 0) } }

    /// restores the selection (or one item); answers restored and conflicts
    public func restore(_ ids: [String]? = nil) async throws -> (restored: Int, conflicts: Int) {
        var r = 0, c = 0
        for id in ids ?? Array(selected) {
            let a = try await services.api.send(API.restore(id))
            r += Int(a.restored ?? 0); c += Int(a.conflicts ?? 0)
        }
        selected = []
        await load()
        return (r, c)
    }

    public func deleteConfirm(_ item: BinItem) -> ConfirmText { Confirms.deleteForever(label: item.label ?? item.id, bytes: item.bytes ?? 0) }

    public func deleteForever(_ id: String) async throws {
        _ = try await services.api.send(API.deleteForever(id))
        await load()
    }

    public func emptyConfirm(vehicleName: String?) -> ConfirmText { Confirms.emptyBin(items: items.count, bytes: totalBytes, vehicleName: vehicleName) }

    public func empty() async throws -> BinEmptyAnswer {
        let a = try await services.api.send(API.emptyBin(vehicle: vehicleID))
        await load()
        return a
    }
}

/// Notifications: every notification, filters, read and dismiss.
@MainActor
@Observable
public final class NotificationsModel {
    public enum Filter: String, Sendable, CaseIterable { case open, unread, all }
    private let services: Services
    public private(set) var all: [AppNotification] = []
    public private(set) var state: LoadState = .idle
    public var filter: Filter = .open
    public var vehicleID: String?

    public init(services: Services) { self.services = services }

    public func load() async {
        state = .loading
        do {
            all = try await services.api.send(API.notifications(vehicle: vehicleID)).notifications ?? []
            state = .loaded
        } catch { state = .failed(errorText(error)) }
    }

    public var shown: [AppNotification] {
        switch filter {
        case .open: return all.filter(\.isOpen)
        case .unread: return all.filter { $0.isOpen && $0.isUnread }
        case .all: return all
        }
    }

    public func markRead(_ ids: [Double]) async throws {
        guard !ids.isEmpty else { return }
        _ = try await services.api.send(API.markRead(ids))
        await load()
    }

    public func markAllRead() async throws { try await markRead(all.filter(\.isUnread).map(\.id)) }

    public func dismiss(_ id: Double) async throws {
        _ = try await services.api.send(API.dismiss(id))
        await load()
    }

    public func dismissAll() async throws {
        for n in all where n.isOpen { _ = try await services.api.send(API.dismiss(n.id)) }
        await load()
    }

    /// which ones the Mac should post as system notifications: new and open
    public static func newlyOpen(previous: [AppNotification], current: [AppNotification]) -> [AppNotification] {
        let seen = Set(previous.map(\.id))
        return current.filter { $0.isOpen && !seen.contains($0.id) }
    }
}

/// The next-run lanes store, one for every skin: conversions at once per
/// device, written by the Encoder form and by Nodes' "Use N at once" (5.1).
public struct LanesStore: Sendable {
    public static let key = "converterLanes"
    public let prefs: PrefsStore

    public init(prefs: PrefsStore) { self.prefs = prefs }

    public func lanes() -> [String: Int] {
        guard let s = prefs.value(Self.key), let d = s.data(using: .utf8),
              let m = try? JSONDecoder().decode([String: Int].self, from: d) else { return [:] }
        return m
    }

    public func set(_ device: String, _ count: Int) {
        update { $0[device] = max(0, min(64, count)) }
    }

    public func update(_ body: (inout [String: Int]) -> Void) {
        var m = lanes()
        body(&m)
        if let d = try? JSONEncoder().encode(m) { prefs.set(Self.key, String(decoding: d, as: UTF8.self)) }
    }
}

/// Nodes: the helper servers, this server as a node, benchmarks and pairing.
@MainActor
@Observable
public final class NodesModel {
    private let services: Services
    public private(set) var nodes: [EncoderNode] = []
    public private(set) var worker: WorkerStatus?
    public private(set) var benchmarks: [String: NodeBenchmark] = [:]
    public private(set) var state: LoadState = .idle
    private let poller: Poller
    public let lanes: LanesStore

    public init(services: Services) {
        self.services = services
        poller = Poller(clock: services.clock)
        lanes = LanesStore(prefs: services.prefs)
    }

    public func load() async {
        do {
            let a = try await services.api.send(API.nodes())
            nodes = a.nodes ?? []
            worker = a.worker
            state = .loaded
        } catch { state = .failed(errorText(error)) }
    }

    /// every 2.5 s while something is moving (pairing, a benchmark, busy), else 5 s
    public var pollInterval: Double {
        let moving = worker?.pairing != nil || benchmarks.values.contains { $0.state == "running" } || nodes.contains { ($0.busy ?? 0) > 0 }
        return moving ? 2.5 : 5
    }

    public func appear() { poller.start(every: 5) { [weak self] in await self?.load() } }
    public func disappear() { poller.stop() }

    public func add(address: String, code: String) async throws -> String? {
        let a = try await services.api.send(API.addNode(address: address, code: code))
        await load()
        return a.id
    }

    public func rename(_ id: String, _ name: String) async throws { _ = try await services.api.send(API.updateNode(id, NodeUpdateBody(name: name))); await load() }
    public func setTrusted(_ id: String, _ on: Bool) async throws { _ = try await services.api.send(API.updateNode(id, NodeUpdateBody(trusted: on))); await load() }
    public func setCars(_ id: String, _ cars: [String]) async throws { _ = try await services.api.send(API.updateNode(id, NodeUpdateBody(cars: cars))); await load() }
    public func remove(_ id: String) async throws { _ = try await services.api.send(API.removeNode(id)); await load() }

    /// "Use N at once": the node's slots, then its row under Encoder for the
    /// next run (the node's default device, or the whole node). Answers the toast.
    public func useAtOnce(_ id: String, _ n: Int) async throws -> String {
        _ = try await services.api.send(API.setNodeSlots(id, n))
        let node = nodes.first { $0.id == id }
        let whole = "node:\(id)"
        let lane = node?.defaultLane?.string ?? whole
        lanes.update { m in
            m[lane] = n
            // the whole-node row of before stands for the same device
            if lane != whole { m.removeValue(forKey: whole) }
        }
        await load()
        if let dev = node?.defaultDeviceName, !dev.isEmpty {
            return "\(dev) on \(node?.name ?? "") now converts \(n) at once; its row under Encoder is set to \(n) too."
        }
        return "The node now converts \(n) at once; its Encoder row is set to \(n) too."
    }

    public func benchmark(_ id: String, codec: String) async throws { benchmarks[id] = try await services.api.send(API.startNodeBenchmark(id, codec: codec)).benchmark }
    public func cancelBenchmark(_ id: String) async throws { benchmarks[id] = try await services.api.send(API.cancelNodeBenchmark(id)).benchmark }
    public func refreshBenchmark(_ id: String) async { benchmarks[id] = (try? await services.api.send(API.nodeBenchmark(id)))?.benchmark ?? benchmarks[id] }

    public func setWorker(enabled: Bool) async throws { worker = try await services.api.send(API.setWorker(enabled: enabled)) }
    public func startPairing() async throws { worker = try await services.api.send(API.startPairing()) }
    public func cancelPairing() async throws { worker = try await services.api.send(API.cancelPairing()) }
    public func forgetMain(_ id: String) async throws { worker = try await services.api.send(API.forgetMain(id)) }
}
