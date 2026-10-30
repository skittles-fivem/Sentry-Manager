import Foundation

/// What the "in your login" launcher, sentry-six-engine, runs (3.3): the
/// daemon plist's environment with this user's paths, plus the agent keys,
/// and node's command line. Pure, so it is tested on Linux.
public struct AgentEnvironment: Sendable, Equatable {
    public let paths: AgentPaths
    /// The bundle's Contents/Resources/engine folder.
    public let engine: String
    public let uid: UInt32

    public init(home: String, engine: String, uid: UInt32) {
        self.paths = AgentPaths(home: home)
        var e = engine
        while e.count > 1 && e.hasSuffix("/") { e.removeLast() }
        self.engine = e
        self.uid = uid
    }

    /// The engine folder from the launcher's own path,
    /// <app>/Contents/MacOS/sentry-six-engine -> <app>/Contents/Resources/engine.
    public static func engineFolder(launcherPath: String) -> String? {
        let parts = launcherPath.split(separator: "/", omittingEmptySubsequences: true)
        guard parts.count >= 3, parts[parts.count - 2] == "MacOS", parts[parts.count - 3] == "Contents" else { return nil }
        let contents = "/" + parts.dropLast(2).joined(separator: "/")
        return "\(contents)/Resources/engine"
    }

    public var node: String { "\(engine)/node/bin/node" }
    public var server: String { "\(engine)/app/server/index.js" }
    public var socket: String { paths.socket(uid: uid) }
    /// True when the socket had to move to /private/tmp (its folder needs checking).
    public var socketMoved: Bool { socket != "\(paths.run)/signin.sock" }

    /// The same keys as the daemon plist's EnvironmentVariables, then the agent's own.
    public var environment: [String: String] {
        [
            "NODE_ENV": "production",
            "APP_ROOT": "\(engine)/app",
            "DATA_PATH": paths.data,
            "RUN_DIR": paths.run,
            "FOOTAGE_ROOT": paths.footage,
            "FFMPEG_PATH": "\(engine)/ffmpeg/ffmpeg",
            "FFPROBE_PATH": "\(engine)/ffmpeg/ffprobe",
            "SENTRY_NOREPLACE_RENAME": "\(engine)/bin/sentry-fsops",
            "HOME": paths.runHome,
            "TMPDIR": paths.tmp,
            "PATH": "/usr/bin:/bin:/usr/sbin:/sbin",
            "UV_THREADPOOL_SIZE": "16",
            "SENTRY_CONSOLE": "off",
            "PUBLIC_INFO_PATH": paths.engineJSON,
            "LOCAL_SIGNIN_SOCKET": socket,
            "SERVICE_MODE": "agent",
        ]
    }

    /// argv for execv: node, its flags, manager.env, the server.
    public var arguments: [String] {
        [node, "--disable-warning=ExperimentalWarning", "--env-file-if-exists=\(paths.managerEnv)", server]
    }

    /// "KEY=value" lines for execve, sorted so the order is stable.
    public var envp: [String] { Self.envp(environment) }

    public static func envp(_ environment: [String: String]) -> [String] {
        environment.sorted { $0.key < $1.key }.map { "\($0.key)=\($0.value)" }
    }
}

/// The launcher's own log rotation: newsyslog does not rotate a user's log (3.3).
public enum LogRotation {
    public static let maxBytes: UInt64 = 10 * 1024 * 1024
    public static let keep = 3

    /// The renames to make, oldest first: service.log.2 -> .3, ... service.log -> .1.
    /// Empty while the log is under the limit.
    public static func plan(log: String, size: UInt64, maxBytes: UInt64 = maxBytes, keep: Int = keep) -> [(from: String, to: String)] {
        guard size >= maxBytes, keep >= 1 else { return [] }
        var moves: [(String, String)] = []
        if keep >= 2 {
            for n in stride(from: keep - 1, through: 1, by: -1) {
                moves.append(("\(log).\(n)", "\(log).\(n + 1)"))
            }
        }
        moves.append((log, "\(log).1"))
        return moves
    }

    /// Runs the plan with FileManager (a missing older log is fine).
    public static func rotate(log: String, fileManager: FileManager = .default) {
        guard let attrs = try? fileManager.attributesOfItem(atPath: log),
              let size = (attrs[.size] as? NSNumber)?.uint64Value else { return }
        for move in plan(log: log, size: size) {
            guard fileManager.fileExists(atPath: move.from) else { continue }
            try? fileManager.removeItem(atPath: move.to)
            try? fileManager.moveItem(atPath: move.from, toPath: move.to)
        }
    }
}
