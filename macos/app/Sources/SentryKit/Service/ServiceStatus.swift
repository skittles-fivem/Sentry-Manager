import Foundation

/// What `launchctl print system/<label>` (or gui/<uid>/<label>) says.
public struct LaunchdJob: Sendable, Equatable {
    public var running: Bool
    public var pid: Int?
    public var lastExitCode: Int?

    public init(running: Bool, pid: Int? = nil, lastExitCode: Int? = nil) {
        self.running = running
        self.pid = pid
        self.lastExitCode = lastExitCode
    }

    /// nil when the job is not loaded (launchctl printed nothing usable).
    public static func parse(_ text: String) -> LaunchdJob? {
        var state: String?
        var pid: Int?
        var code: Int?
        for raw in text.split(separator: "\n") {
            let parts = raw.split(separator: "=", maxSplits: 1).map { $0.trimmingCharacters(in: .whitespaces) }
            guard parts.count == 2 else { continue }
            switch parts[0] {
            // the first of each: nested blocks repeat some keys
            case "state" where state == nil: state = parts[1]
            case "pid" where pid == nil: pid = Int(parts[1])
            // "0", or "78: EX_CONFIG"; "(never exited)" is no code
            case "last exit code" where code == nil: code = Int(String(parts[1].prefix { $0.isNumber || $0 == "-" }))
            default: break
            }
        }
        guard let state else { return nil }
        return LaunchdJob(running: state == "running", pid: pid, lastExitCode: code)
    }
}

/// The menu bar's status line (section 4).
public enum EngineState: Sendable, Equatable {
    case running, starting, stopped, notResponding

    public var text: String {
        switch self {
        case .running: return "Sentry Six Manager is running"
        case .starting: return Strings.Shell.starting
        case .stopped: return Strings.Shell.stopped
        case .notResponding: return "Not responding"
        }
    }
}

/// Decides the state from /healthz and launchd, polled every 10 s (15 s on battery).
public struct EngineStateTracker: Sendable {
    public static let startingWindow: TimeInterval = 60
    public static let failuresBeforeNotResponding = 3

    public private(set) var failures = 0
    public private(set) var startedAt: Date?

    public init() {}

    /// A Start (or the first open) began now: "Starting…" for up to a minute.
    public mutating func didStart(at now: Date) {
        startedAt = now
        failures = 0
    }

    public mutating func update(healthOK: Bool, launchdRunning: Bool?, now: Date) -> EngineState {
        if healthOK {
            failures = 0
            startedAt = nil
            return .running
        }
        failures += 1
        if let s = startedAt, now.timeIntervalSince(s) < Self.startingWindow { return .starting }
        startedAt = nil
        guard launchdRunning == true else { return .stopped }
        return failures >= Self.failuresBeforeNotResponding ? .notResponding : .starting
    }

    public static func pollInterval(onBattery: Bool) -> TimeInterval { onBattery ? 15 : 10 }
}

/// The menu bar's words that depend on numbers (section 4).
public enum MenuBarText {
    public static func conversions(_ count: Int?) -> String {
        guard let count else { return "Sign in to see conversions" }
        if count == 0 { return "No conversions running" }
        return "Converting: \(count) \(count == 1 ? "job" : "jobs")"
    }

    /// Asked before Stop while conversions run; nil means stop at once.
    public static func stopConfirm(running: Int) -> String? {
        guard running > 0 else { return nil }
        if running == 1 { return "Stop Sentry Six Manager? 1 conversion is running. It pauses safely and continues when it starts again." }
        return "Stop Sentry Six Manager? \(running) conversions are running. They pause safely and continue when it starts again."
    }

    public static let open = "Open Sentry Six Manager"
    public static let start = "Start"
    public static let stop = "Stop"
    public static let setupCode = "Setup code…"
    public static let logs = "Logs"
    public static let showServiceLog = "Show service log in Finder"
    public static let quit = "Quit (the service keeps running)"
    public static let criticalUpdate = "Critical update: install now"
}

/// The app's own lines around the administrator prompt and the service (3.1–3.7).
public enum ServiceText {
    public enum AdminAction: Sendable {
        case install, update, start, stop, setupCode, allowFolder, localSignIn, switchMode, uninstall
    }

    /// Shown just before macOS asks for the password.
    public static func adminReason(_ a: AdminAction) -> String {
        let why: String
        switch a {
        case .install: why = "to install the background service"
        case .update: why = "to update the service"
        case .start: why = "to start the service"
        case .stop: why = "to stop the service"
        case .setupCode: why = "to show the setup code"
        case .allowFolder: why = "to let the service read this folder"
        case .localSignIn: why = "to change who signs in without a password"
        case .switchMode: why = "to change how Sentry Six Manager runs"
        case .uninstall: why = "to remove the background service"
        }
        return "Your administrator password is needed \(why)."
    }

    public static let firstOpenTitle = "How should Sentry Six Manager run?"
    public static let daemonTitle = "Background service (recommended)"
    public static let daemonBody = "Runs when the Mac starts, even when nobody is logged in. It runs as a hidden account that cannot read your home folder. Needs your administrator password once."
    public static let agentTitle = "In your login"
    public static let agentBody = "Runs only while you are logged in, as you. No administrator password. It stops when you log out."
    public static let changeLater = "You can change this later in Settings › This Mac."
    public static let continueButton = "Continue"
    public static let quitButton = "Quit"
    public static let alreadyInstalled = "Sentry Six Manager is already installed as a background service on this Mac. This app will use it."
    public static let updateService = "Update the service"
    public static let starting = "Starting Sentry Six Manager…"
    public static let notificationsAsk = "Sentry Six Manager can tell you when a share goes offline, a conversion fails or an update is out."
    public static let allow = "Allow"
    public static let notNow = "Not now"
    public static let loginItemsApproval = "Allow Sentry Six Manager under System Settings › General › Login Items."
    public static let restartingNewVersion = "Restarting Sentry Six Manager with the new version…"
    public static let newerInApp = "This app has a newer Sentry Six Manager. Update the service?"
    public static let update = "Update"
    public static let later = "Later"
    public static let serviceNewer = "The service is newer than this app. Download the new app."
    public static let howItRuns = "How it runs"
    public static let change = "Change…"
    public static let uninstall = "Uninstall…"
    public static let uninstallConfirm = "Remove Sentry Six Manager from this Mac? The service stops. Your footage on drives and shares is never touched."
    public static let keepData = "Keep my settings and data"
    public static let removeAll = "Remove everything"
    public static let binStays = "Items in the Recycle Bin stay on the share in the .sentry-six-bin folder."
    public static let uninstallDone = "Done. Drag Sentry Six Manager from Applications to the Bin to finish."
    public static let serviceKeptData = "The service already has its own data. It was kept; yours stays in your login folder."

    public static func modeName(_ m: ServiceMode) -> String {
        m == .daemon ? "Background service" : "In your login"
    }

    /// After a switch to the service: a folder vehicle in the home folder (3.6).
    public static func cannotRead(vehicle: String) -> String {
        "\(vehicle): the service can't read this folder. Move the footage to /Users/Shared or a drive, or choose Let the service read this folder."
    }
}

/// Which way an installed engine and the app's bundled one compare (3.5).
public enum EngineVersionCompare: Sendable, Equatable {
    case same, appNewer, serviceNewer

    /// `sha` decides sameness; versions (dotted numbers) decide which is newer.
    public static func compare(appVersion: String, appSha: String, engineVersion: String?, engineSha: String?) -> EngineVersionCompare {
        if let s = engineSha, s == appSha { return .same }
        guard let ev = engineVersion else { return .appNewer }
        switch dotted(ev, appVersion) {
        case .orderedDescending: return .serviceNewer
        default: return .appNewer
        }
    }

    static func dotted(_ a: String, _ b: String) -> ComparisonResult {
        let x = a.split(whereSeparator: { !$0.isNumber }).map { Int($0) ?? 0 }
        let y = b.split(whereSeparator: { !$0.isNumber }).map { Int($0) ?? 0 }
        for i in 0..<max(x.count, y.count) {
            let l = i < x.count ? x[i] : 0
            let r = i < y.count ? y[i] : 0
            if l != r { return l < r ? .orderedAscending : .orderedDescending }
        }
        return .orderedSame
    }
}
