import Foundation

/// Time for timers, backoff and the sync logic, injected so tests can run
/// them without waiting.
public protocol AppClock: Sendable {
    /// seconds since some fixed point
    func now() -> Double
    func sleep(_ seconds: Double) async throws
}

public struct SystemClock: AppClock {
    public init() {}
    public func now() -> Double { ProcessInfo.processInfo.systemUptime }
    public func sleep(_ seconds: Double) async throws {
        try await Task.sleep(nanoseconds: UInt64(max(0, seconds) * 1_000_000_000))
    }
}

/// A clock that only moves when told to; sleepers wake when their time comes.
public final class ManualClock: AppClock, @unchecked Sendable {
    private struct Sleeper { var until: Double; var cont: CheckedContinuation<Void, Error> }
    private let lock = NSLock()
    private var time: Double
    private var sleepers: [Int: Sleeper] = [:]
    private var nextID = 0
    private var cancelled = Set<Int>()
    /// every sleep asked for, in order
    public private(set) var sleeps: [Double] = []

    public init(start: Double = 0) { time = start }

    public func now() -> Double { lock.lock(); defer { lock.unlock() }; return time }

    public func sleep(_ seconds: Double) async throws {
        let id = register(seconds)
        try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (c: CheckedContinuation<Void, Error>) in
                lock.lock()
                if cancelled.remove(id) != nil { lock.unlock(); c.resume(throwing: CancellationError()); return }
                if seconds <= 0 { lock.unlock(); c.resume(); return }
                sleepers[id] = Sleeper(until: time + seconds, cont: c)
                lock.unlock()
            }
        } onCancel: {
            lock.lock()
            let s = sleepers.removeValue(forKey: id)
            if s == nil { cancelled.insert(id) }
            lock.unlock()
            s?.cont.resume(throwing: CancellationError())
        }
    }

    /// moves time on and wakes whoever is due
    private func register(_ seconds: Double) -> Int {
        lock.lock()
        defer { lock.unlock() }
        nextID += 1
        sleeps.append(seconds)
        return nextID
    }

    public func advance(_ seconds: Double) {
        lock.lock()
        time += seconds
        let due = sleepers.filter { $0.value.until <= time + 1e-9 }
        for k in due.keys { sleepers.removeValue(forKey: k) }
        lock.unlock()
        for s in due.values.sorted(by: { $0.until < $1.until }) { s.cont.resume() }
    }

    public var pendingSleepers: Int { lock.lock(); defer { lock.unlock() }; return sleepers.count }
}
