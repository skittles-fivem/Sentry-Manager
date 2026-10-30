import Foundation
import Observation

/// The Encoder page: the conversion on show (kept by ConverterTracker),
/// its pace, the devices and the automatic settings, and the commands.
@MainActor
@Observable
public final class EncoderModel {
    private let services: Services
    public let clientID: String
    public private(set) var tracker = ConverterTracker()
    private var meter = ThroughputMeter()
    public private(set) var pace = ThroughputMeter.Pace()
    public private(set) var capabilities: ConverterCapabilities?
    public private(set) var devices: ConverterDevices?
    public private(set) var auto: ConverterAuto?
    public private(set) var error: String?
    /// the converter's error codes, for the page's own wording tables
    public private(set) var lastFailure: ConverterError?
    public private(set) var benchmarkProgress: JSONValue?
    /// per device, the last 90 fps samples
    public private(set) var fpsHistory: [String: RingBuffer] = [:]
    private let autoPoller: Poller
    /// the concurrency stepper waits 350 ms before it sends
    private var concurrencyTask: Task<Void, Never>?
    public let lanes: LanesStore

    public init(services: Services, clientID: String) {
        self.services = services
        self.clientID = clientID
        autoPoller = Poller(clock: services.clock)
        lanes = LanesStore(prefs: services.prefs)
    }

    public var snapshot: ConverterSnapshot? { tracker.snapshot }

    private func call<V: Decodable & Sendable>(_ name: String, _ payload: (any Encodable & Sendable)? = nil, as: V.Type, timeout: RequestTimeout = .normal) async -> V? {
        let body = payload.flatMap { try? JSON.encode(AnyEncodable($0)) }
        do {
            switch try await services.api.invoke(API.invoke(name, clientID: clientID, payload: body, as: V.self, timeout: timeout)) {
            case .value(let v): lastFailure = nil; return v
            case .failure(let f): lastFailure = f; return nil
            }
        } catch {
            self.error = errorText(error)
            return nil
        }
    }

    /// opening the page: capabilities, the current job, devices, automatic
    public func appear() async {
        capabilities = await call("getCapabilities", as: ConverterCapabilities.self)
        if let snap = await call("getCurrent", as: ConverterSnapshot.self) { tracker.apply(snap, revision: snap.revision) }
        devices = await call("devices", as: ConverterDevices.self)
        auto = await call("getAuto", as: ConverterAuto.self)
        autoPoller.start(every: 30) { [weak self] in
            guard let self else { return }
            self.auto = await self.call("getAuto", as: ConverterAuto.self) ?? self.auto
        }
        render()
    }

    public func disappear() { autoPoller.stop() }

    /// an update from ConverterStream
    public func receive(_ u: ConverterStream.Update) {
        switch u {
        case .progress(let e):
            if tracker.handle(e) { recordFPS() }
        case .current(let s):
            if let s { tracker.apply(s, revision: s.revision) }
        case .benchmark(let v):
            benchmarkProgress = v
        case .state, .unauthorized:
            break
        }
        render()
    }

    private func recordFPS() {
        guard let id = tracker.snapshot?.id, let f = tracker.fps[id] else { return }
        let device = tracker.snapshot?.config?.device ?? "default"
        var ring = fpsHistory[device] ?? RingBuffer(keep: 90)
        ring.append(f)
        fpsHistory[device] = ring
    }

    /// counts and the pace, worked out once per render
    public func render() {
        let finished = tracker.count("completed") + tracker.count("failed")
        let converting = tracker.count("active")
        let remaining = tracker.count("queued") + converting
        pace = meter.pace(snapshot: tracker.snapshot, done: finished, remaining: remaining, now: services.nowMs())
    }

    // MARK: commands

    public func start(_ config: ConverterConfig) async -> Bool {
        tracker.startBegan()
        guard let a = await call("start", config, as: ConverterStartAnswer.self), let id = a.jobId else {
            tracker.startFailed()
            return false
        }
        tracker.startAnswered(jobID: id, config: config, now: services.nowMs())
        render()
        return true
    }

    struct JobBody: Encodable, Sendable { var jobId: String }
    struct ConcurrencyBody: Encodable, Sendable { var jobId: String; var concurrency: Int }
    struct LanesBody: Encodable, Sendable { var jobId: String; var lanes: [String: Int] }

    /// pause, resume, cancel, discard, retryFailed, dismissSummary
    public func command(_ name: String) async {
        guard let id = tracker.snapshot?.id else { return }
        if let next = await call(name, JobBody(jobId: id), as: ConverterSnapshot.self) { tracker.apply(next, revision: next.revision) }
        render()
    }

    /// the live stepper: the last value wins, 350 ms after the last change
    public func setConcurrency(_ n: Int) {
        concurrencyTask?.cancel()
        let clock = services.clock
        concurrencyTask = Task { @MainActor [weak self] in
            do { try await clock.sleep(0.35) } catch { return }
            guard let self, let id = self.tracker.snapshot?.id else { return }
            if let next = await self.call("setConcurrency", ConcurrencyBody(jobId: id, concurrency: n), as: ConverterSnapshot.self) {
                self.tracker.apply(next, revision: next.revision)
            }
        }
    }

    public func setLanes(_ map: [String: Int]) async {
        for (k, v) in map { lanes.set(k, v) }
        guard let id = tracker.snapshot?.id else { return }
        if let next = await call("setLanes", LanesBody(jobId: id, lanes: map), as: ConverterSnapshot.self) { tracker.apply(next, revision: next.revision) }
    }

    /// the long benchmark: one request that stays open for minutes (8.4)
    public func runBenchmark(_ request: JSONValue) async -> JSONValue? {
        await call("runBenchmark", request, as: JSONValue.self, timeout: .benchmark)
    }

    public func cancelBenchmark() async { _ = await call("cancelBenchmark", as: JSONValue.self) }
    public func setAuto(_ settings: JSONValue) async { auto = await call("setAuto", settings, as: ConverterAuto.self) ?? auto }
    public func checkAutoNow() async { _ = await call("checkAutoNow", as: JSONValue.self) }

    /// phase timings per file, largest first (the web's renderTimings)
    public var timings: [(phase: String, averageMs: Double)] {
        guard let t = tracker.snapshot?.timings, let files = t["files"]?.number, files > 0, let totals = t["totalMs"]?.object else { return [] }
        return totals.compactMap { k, v in v.number.flatMap { $0 > 0 ? (k, $0 / files) : nil } }.sorted { $0.1 > $1.1 }
    }
}

/// Wraps any Encodable so it can be passed through the call helper.
struct AnyEncodable: Encodable {
    let value: any Encodable
    init(_ v: any Encodable) { value = v }
    func encode(to encoder: Encoder) throws { try value.encode(to: encoder) }
}
