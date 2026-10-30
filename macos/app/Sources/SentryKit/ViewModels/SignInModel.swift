import Foundation
import Observation

/// The sign-in and create-account sheet (6.1), shared by every skin.
@MainActor
@Observable
public final class SignInModel {
    public enum Mode: Sendable, Equatable { case signIn, create, insecure }

    private let services: Services
    public private(set) var mode: Mode = .signIn
    public private(set) var fingerprint: String?
    public var username = "admin"
    public var password = ""
    public var confirmPassword = ""
    public var setupCode = ""
    public private(set) var error: String?
    public private(set) var busy = false
    /// seconds left of a "Try again in N s." answer
    public private(set) var waitSeconds = 0
    private var countdown: Task<Void, Never>?
    public private(set) var signedInUser: String?

    public init(services: Services) { self.services = services }

    /// GET /api/auth/me: which form shows
    public func load() async {
        do {
            let me = try await services.api.send(API.me())
            fingerprint = me.fingerprint
            signedInUser = me.user
            if me.secure == false { mode = .insecure }
            else if me.needsSetup == true { mode = .create; username = "" }
            else { mode = .signIn }
        } catch {
            self.error = Strings.SignIn.couldNotReach
        }
    }

    public func fillSetupCode(_ code: String) { setupCode = code; mode = .create }

    /// the client checks before Create account, in the web's order
    public func validateCreate() -> String? {
        if password != confirmPassword { return Strings.SignIn.mismatch }
        if password.count < 8 { return Strings.SignIn.tooShort }
        return nil
    }

    public var canSubmit: Bool { !busy && waitSeconds == 0 && mode != .insecure }

    @discardableResult
    public func submit() async -> Bool {
        guard canSubmit else { return false }
        error = nil
        if mode == .create, let problem = validateCreate() { error = problem; return false }
        busy = true
        defer { busy = false }
        do {
            if mode == .create {
                _ = try await services.api.send(API.setup(username: username, password: password, code: setupCode.trimmingCharacters(in: .whitespaces)))
            } else {
                _ = try await services.api.send(API.login(username: username, password: password))
            }
            password = ""; confirmPassword = ""
            signedInUser = username
            return true
        } catch let e as APIError {
            switch e {
            case .unreachable, .timedOut: error = Strings.SignIn.couldNotReach
            default: error = e.message
            }
            if case .http(429, let m, _) = e, let n = Self.retrySeconds(m) { startCountdown(n) }
            return false
        } catch {
            self.error = Strings.SignIn.couldNotReach
            return false
        }
    }

    /// "Too many attempts. Try again in 30 s." -> 30
    public static func retrySeconds(_ message: String) -> Int? {
        guard let r = message.range(of: #"Try again in (\d+) s"#, options: .regularExpression) else { return nil }
        return Int(message[r].filter(\.isNumber))
    }

    private func startCountdown(_ seconds: Int) {
        countdown?.cancel()
        waitSeconds = seconds
        let clock = services.clock
        countdown = Task { @MainActor in
            while waitSeconds > 0 {
                do { try await clock.sleep(1) } catch { return }
                waitSeconds -= 1
            }
        }
    }
}

/// The menu bar's data (section 4): the status line, decided by
/// EngineStateTracker from /healthz and launchd, and the conversions count
/// from /api/jobs and the `jobs` topic. Its words are MenuBarText's.
@MainActor
@Observable
public final class MenuBarModel {
    public private(set) var state: EngineState = .stopped
    /// nil while signed out
    public private(set) var conversions: Int?
    public private(set) var needsSetup = false
    private var tracker = EngineStateTracker()

    public init() {}

    /// A Start (or the first open) began now: "Starting…" for up to a minute.
    public func didStart(at now: Date) { tracker.didStart(at: now) }

    /// One poll: /healthz answered "ok" or not, and what launchd says (nil: unknown).
    public func health(ok: Bool, launchdRunning: Bool?, now: Date) {
        state = tracker.update(healthOK: ok, launchdRunning: launchdRunning, now: now)
    }

    public func update(jobs: [Job]) { conversions = jobs.runningConversions }
    public func update(me: AuthMe) {
        needsSetup = me.needsSetup == true
        if me.user == nil { conversions = nil }
    }
    public func signedOut() { conversions = nil }

    public var statusText: String { state.text }
    public var conversionsText: String { MenuBarText.conversions(conversions) }
    /// asked before Stop while conversions run; nil means stop at once
    public var stopConfirm: String? { MenuBarText.stopConfirm(running: conversions ?? 0) }
}
