import Foundation

/// How the engine runs (docs/plans/macos-app.md, section 3).
public enum ServiceMode: String, Sendable, CaseIterable, Codable {
    /// A LaunchDaemon as the hidden account _sentrysix, from /Library/SentrySixManager.
    case daemon
    /// A LaunchAgent in the user's login, from inside the app bundle.
    case agent

    /// The app's record of the first-open choice in UserDefaults (2.3 step 1).
    public static let defaultsKey = "serviceMode"
}

/// Names and paths both modes share with install.sh and the plists.
public enum ServiceNames {
    public static let bundleID = "io.github.skittles-fivem.sentry-six-manager"
    /// The LaunchDaemon's label (install.sh, the Terminal install).
    public static let daemonLabel = "io.github.skittles-fivem.sentry-six-manager"
    /// The LaunchAgent's label: a different one, so the two are never confused.
    public static let agentLabel = "io.github.skittles-fivem.sentry-six-manager.agent"
    public static var agentPlistName: String { "\(agentLabel).plist" }
    public static let serviceAccount = "_sentrysix"
    public static let defaultPort = 9000
}

/// The background service's fixed paths (install.sh).
public enum DaemonPaths {
    public static let program = "/Library/SentrySixManager"
    public static let state = "/Library/Application Support/SentrySixManager"
    public static let publicDir = "/Library/Application Support/SentrySixManager Public"
    public static let localDir = "/Library/Application Support/SentrySixManager Local"
    public static let plist = "/Library/LaunchDaemons/\(ServiceNames.daemonLabel).plist"
    public static let log = "/Library/Logs/SentrySixManager/service.log"
    public static var cli: String { "\(program)/bin/sentry-six-manager" }
    public static var uninstall: String { "\(program)/uninstall.sh" }
    public static var engineJSON: String { "\(publicDir)/engine.json" }
    public static var socket: String { "\(localDir)/signin.sock" }
    public static var data: String { "\(state)/data" }
}

/// The "in your login" paths, from the user's home folder (3.3).
public struct AgentPaths: Sendable, Equatable {
    public let home: String

    public init(home: String) {
        var h = home
        while h.count > 1 && h.hasSuffix("/") { h.removeLast() }
        self.home = h
    }

    public var state: String { "\(home)/Library/Application Support/SentrySixManager" }
    public var data: String { "\(state)/data" }
    public var run: String { "\(state)/run" }
    public var tmp: String { "\(run)/tmp" }
    public var runHome: String { "\(run)/home" }
    public var footage: String { "\(state)/footage" }
    public var publicDir: String { "\(state)/public" }
    public var engineJSON: String { "\(publicDir)/engine.json" }
    public var managerEnv: String { "\(state)/manager.env" }
    public var setupCode: String { "\(data)/setup-code.txt" }
    public var logDir: String { "\(home)/Library/Logs/SentrySixManager" }
    public var log: String { "\(logDir)/service.log" }
    /// A LaunchAgent written by hand when SMAppService refuses (unsigned builds, 3.2).
    public var fallbackAgentPlist: String { "\(home)/Library/LaunchAgents/\(ServiceNames.agentPlistName)" }

    /// Folders the launcher makes, each with its mode: 0700, public/ 0755.
    public var folders: [(path: String, mode: Int)] {
        [(state, 0o700), (data, 0o700), (run, 0o700), (tmp, 0o700), (runHome, 0o700),
         (footage, 0o700), (publicDir, 0o755), (logDir, 0o700)]
    }

    /// The sign-in socket: in run/, or /private/tmp/sentrysix-<uid>/ when
    /// the path would not fit sun_path (103 bytes on macOS, 2.6).
    public func socket(uid: UInt32) -> String {
        let normal = "\(run)/signin.sock"
        return SocketPath.fits(normal) ? normal : SocketPath.shortFallback(uid: uid)
    }
}

public enum SocketPath {
    /// sun_path holds 104 bytes on macOS, one of them the NUL.
    public static let maxBytes = 103

    public static func fits(_ path: String) -> Bool { path.utf8.count <= maxBytes }

    public static func shortFallbackDir(uid: UInt32) -> String { "/private/tmp/sentrysix-\(uid)" }
    public static func shortFallback(uid: UInt32) -> String { "\(shortFallbackDir(uid: uid))/signin.sock" }
}
