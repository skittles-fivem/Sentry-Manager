import Foundation
import Observation

/// What every view model is given: the API, a clock for timers, the
/// per-Mac-user settings and the wall time (epoch ms) for "ago" texts.
public struct Services: Sendable {
    public var api: APIClient
    public var clock: AppClock
    public var prefs: PrefsStore
    public var nowMs: @Sendable () -> Double

    public init(api: APIClient, clock: AppClock = SystemClock(), prefs: PrefsStore,
                nowMs: @escaping @Sendable () -> Double = { Date().timeIntervalSince1970 * 1000 }) {
        self.api = api; self.clock = clock; self.prefs = prefs; self.nowMs = nowMs
    }
}

/// A page's loading state, as every page shows it.
public enum LoadState: Sendable, Equatable {
    case idle, loading, loaded
    case failed(String)
}

/// A repeating timer tied to a view's lifetime; nothing runs while stopped.
@MainActor
public final class Poller {
    private var task: Task<Void, Never>?
    private let clock: AppClock

    public init(clock: AppClock) { self.clock = clock }

    /// runs `body` now and then every `interval` seconds; a run never starts
    /// while the last one is still out
    public func start(every interval: Double, _ body: @escaping @MainActor () async -> Void) {
        stop()
        let clock = self.clock
        task = Task { @MainActor in
            while !Task.isCancelled {
                await body()
                do { try await clock.sleep(interval) } catch { return }
            }
        }
    }

    public func stop() { task?.cancel(); task = nil }
    public var running: Bool { task != nil }
}

/// Text for an error, as the web shows it (server words as they are).
public func errorText(_ e: Error) -> String {
    if let a = e as? APIError { return a.message }
    return Strings.Shell.cantReach
}
