import Foundation

/// The converter's states, as footageConverter.js groups them.
public enum ConverterStates {
    public static let active: Set<String> = ["discovering", "waiting", "staging", "encoding", "writing-telemetry",
                                            "verifying", "copying", "replacing", "paused", "paused_network"]
    public static let paused: Set<String> = ["paused", "paused_network"]
    public static let finished: Set<String> = ["completed", "failed", "cancelled"]

    /// time the job spent working, without paused time
    public static func workingMs(_ job: ConverterSnapshot?, at: Double) -> Double {
        guard let job, let created = job.createdAt else { return 0 }
        let state = job.state ?? ""
        let stopped = !active.contains(state) || paused.contains(state)
        let end = stopped ? (job.updatedAt ?? created) : at
        return max(0, end - created - (job.pausedMs ?? 0))
    }
}

/// Which conversion the Encoder shows, kept from the stream's snapshots
/// exactly as the web's converter does (8.3).
public struct ConverterTracker: Sendable, Equatable {
    public private(set) var snapshot: ConverterSnapshot?
    public private(set) var latestRevision: Double = -1
    /// set while Start waits for its answer: events of unknown jobs wait here
    private var pending: [String: Buffered]?
    private var pendingOrder: [String] = []
    /// the latest fps and node wait per job
    public private(set) var fps: [String: Double] = [:]
    public private(set) var nodeWaiting: [String: (count: Double, encoding: Double?)] = [:]

    struct Buffered: Sendable, Equatable { var full: ConverterProgressEvent?; var progress: ConverterProgressEvent? }

    public init() {}

    public static func == (a: ConverterTracker, b: ConverterTracker) -> Bool {
        a.snapshot == b.snapshot && a.latestRevision == b.latestRevision && a.pending == b.pending && a.fps == b.fps
    }

    /// a whole snapshot (getCurrent, a command's answer, a stream event)
    @discardableResult
    public mutating func apply(_ next: ConverterSnapshot?, revision: Double?, progressOnly: Bool = false) -> Bool {
        guard let next, let revision, revision >= latestRevision else { return false }
        let jobID = next.id ?? snapshot?.id
        if let p = next.progress {
            if let f = p["fps"]?.number, f.isFinite, let jobID { fps[jobID] = f }
            if let w = p["waitingOnNode"]?.number, let jobID { nodeWaiting[jobID] = (w, p["encodingNow"]?.number) }
        }
        if progressOnly, snapshot != nil {
            snapshot!.progress = next.progress
        } else {
            snapshot = next
        }
        latestRevision = revision
        return true
    }

    /// a converter:progress event
    @discardableResult
    public mutating func handle(_ e: ConverterProgressEvent) -> Bool {
        guard let jobID = e.jobId, let snap = e.snapshot else { return false }
        if pending != nil && (snapshot?.id == nil || jobID != snapshot?.id) {
            var b = pending![jobID]
            if b == nil {
                if pending!.count >= 8 { return false }
                b = Buffered()
                pendingOrder.append(jobID)
            }
            if e.progressOnly {
                if b!.progress == nil || (e.revision ?? -1) >= (b!.progress!.revision ?? -1) { b!.progress = e }
            } else {
                if b!.full == nil || (e.revision ?? -1) >= (b!.full!.revision ?? -1) { b!.full = e }
            }
            pending![jobID] = b
            return false
        }
        if let current = snapshot?.id, jobID != current {
            // a new job (an automatic one) replaces a finished one on screen
            if e.progressOnly || !ConverterStates.finished.contains(snapshot?.state ?? "") { return false }
            snapshot = nil
            latestRevision = -1
        }
        return apply(snap, revision: e.revision, progressOnly: e.progressOnly)
    }

    /// Start was pressed: hold events of jobs not known yet
    public mutating func startBegan() {
        pending = [:]
        pendingOrder = []
    }

    /// Start answered: a placeholder first, then what came for that job meanwhile
    public mutating func startAnswered(jobID: String, config: ConverterConfig?, now: Double) {
        let buffered = pending?[jobID]
        pending = nil
        pendingOrder = []
        snapshot = nil
        latestRevision = -1
        var placeholder = ConverterSnapshot()
        placeholder.id = jobID
        placeholder.state = "discovering"
        placeholder.revision = 0
        placeholder.createdAt = now
        placeholder.updatedAt = now
        placeholder.config = config
        placeholder.items = []
        placeholder.counters = ConverterCounters(sourceVideoBytes: 0)
        apply(placeholder, revision: 0)
        if let f = buffered?.full { apply(f.snapshot, revision: f.revision, progressOnly: false) }
        if let p = buffered?.progress { apply(p.snapshot, revision: p.revision, progressOnly: true) }
    }

    public mutating func startFailed() { pending = nil; pendingOrder = [] }

    public var isStarting: Bool { pending != nil }

    // MARK: counts (large jobs come truncated: the counts come from the server)

    public func count(_ state: String) -> Int {
        guard let s = snapshot else { return 0 }
        if let c = s.itemStateCounts { return Int(c[state] ?? 0) }
        return (s.items ?? []).filter { $0.state == state }.count
    }

    public var itemCount: Int { Int(snapshot?.itemCount ?? Double(snapshot?.items?.count ?? 0)) }
    public var isActive: Bool { ConverterStates.active.contains(snapshot?.state ?? "") }
    public var isPaused: Bool { ConverterStates.paused.contains(snapshot?.state ?? "") }
}

/// Files finished per minute over the last 3 minutes, falling back to the
/// whole run while that window is short; reset on pause and on a new job.
public struct ThroughputMeter: Sendable, Equatable {
    public static let windowMs: Double = 3 * 60 * 1000
    public static let minSpanMs: Double = 10 * 1000

    struct Sample: Sendable, Equatable { var time: Double; var done: Int }
    private var samples: [Sample] = []
    private var jobID: String?

    public init() {}

    public struct Pace: Sendable, Equatable {
        public var perMinute: Double?
        public var remainingMs: Double?
    }

    /// one render: `done` = completed + failed, `remaining` = queued + converting
    public mutating func pace(snapshot: ConverterSnapshot?, done: Int, remaining: Int, now: Double) -> Pace {
        let state = snapshot?.state ?? ""
        if ConverterStates.paused.contains(state) { samples = [] }
        let active = ConverterStates.active.contains(state)
        let paused = ConverterStates.paused.contains(state)
        var pace = active && !paused ? throughput(snapshot, done: done, remaining: remaining, now: now) : Pace()
        if !active && state == "completed" && done > 0 {
            let minutes = ConverterStates.workingMs(snapshot, at: now) / 60000
            if minutes > 0 { pace = Pace(perMinute: Double(done) / minutes, remainingMs: nil) }
        }
        return pace
    }

    mutating func throughput(_ snapshot: ConverterSnapshot?, done: Int, remaining: Int, now time: Double) -> Pace {
        if snapshot?.id != jobID {
            jobID = snapshot?.id
            samples = []
        }
        if samples.isEmpty || samples[samples.count - 1].done != done { samples.append(Sample(time: time, done: done)) }
        while samples.count > 2 && time - samples[1].time > Self.windowMs { samples.removeFirst() }
        let first = samples[0]
        var perMinute: Double?
        let working = ConverterStates.workingMs(snapshot, at: time)
        if samples.count > 1 && time - first.time >= Self.minSpanMs && done > first.done {
            perMinute = Double(done - first.done) / ((time - first.time) / 60000)
        } else if snapshot?.createdAt != nil && done > 0 && working >= Self.minSpanMs {
            perMinute = Double(done) / (working / 60000)
        }
        guard let pm = perMinute, pm.isFinite, pm != 0 else { return Pace() }
        return Pace(perMinute: pm, remainingMs: remaining > 0 ? Double(remaining) / pm * 60000 : 0)
    }
}
