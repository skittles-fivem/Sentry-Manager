import Foundation

/// engine.json, the engine's public file (2.3).
public struct EngineInfo: Codable, Sendable, Equatable {
    public var v: Double?
    public var port: Double?
    public var pid: Double?
    public var version: String?
    public var sha: String?
    public var tls: EngineTLS?
    public var writtenAt: Double?

    public static let knownVersion: Double = 1

    /// the port, when it is a real one (1...65535)
    public var portNumber: Int? {
        guard let port, let n = Int(exactly: port), (1...65535).contains(n) else { return nil }
        return n
    }

    public var baseURL: URL? {
        guard let portNumber else { return nil }
        return URL(string: "https://127.0.0.1:\(portNumber)")
    }

    /// The file without the ownership and certificate checks: the version
    /// this app knows and a real port; `tls` may be null (TLS_MODE=off).
    /// For the service side (the port to ask /healthz on, the version to
    /// compare with the app's own engine). Signing in goes through
    /// EngineLocator instead.
    public static func decode(_ data: Data) -> EngineInfo? {
        guard let info = try? JSON.decode(EngineInfo.self, from: data), info.v == knownVersion, info.portNumber != nil else { return nil }
        return info
    }
}

public struct EngineTLS: Codable, Sendable, Equatable {
    /// 'local-ca' or 'own'
    public var mode: String?
    public var caPem: String?
    public var caSha256: String?
    public var leafPem: String?
    public var leafSha256: String?
}

/// Where the public file, the setup code and the same-Mac sign-in socket
/// live for a mode: the fixed paths of DaemonPaths (install.sh) or the
/// user's AgentPaths (the launcher).
public struct EnginePaths: Sendable, Equatable {
    public var mode: ServiceMode
    public var home: String

    public init(mode: ServiceMode, home: String) { self.mode = mode; self.home = home }

    private var agent: AgentPaths { AgentPaths(home: home) }

    public var publicFolder: String { mode == .daemon ? DaemonPaths.publicDir : agent.publicDir }
    public var engineJSON: String { mode == .daemon ? DaemonPaths.engineJSON : agent.engineJSON }
    public var log: String { mode == .daemon ? DaemonPaths.log : agent.log }

    /// the setup code file, readable by the app only in login mode
    public var setupCodeFile: String? { mode == .agent ? agent.setupCode : nil }

    /// The same-Mac sign-in socket (2.6); a too long login path falls back
    /// to /private/tmp/sentrysix-<uid>/signin.sock (the launcher makes it).
    public func socket(uid: UInt32) -> String { mode == .daemon ? DaemonPaths.socket : agent.socket(uid: uid) }
}

extension FileChecking {
    /// The uid the engine's folders and files must belong to: _sentrysix
    /// for the service, the user in login mode.
    public func engineOwner(_ mode: ServiceMode) -> UInt32? {
        mode == .daemon ? uid(ofUser: ServiceNames.serviceAccount) : currentUID
    }
}

/// Why engine.json was not used.
public enum EngineLookup: Sendable, Equatable {
    case found(EngineInfo)
    /// no file yet: the engine is starting or stopped
    case missing
    /// a file the app must not trust; the reason is for the log
    case refused(String)
    /// a newer engine.json than this app knows
    case tooNew
}

/// Reads and checks engine.json (2.3 steps 2 and 3).
public struct EngineLocator: Sendable {
    public var paths: EnginePaths
    public var fs: FileChecking

    public init(paths: EnginePaths, fs: FileChecking = PosixFileSystem()) { self.paths = paths; self.fs = fs }

    public func lookup() -> EngineLookup {
        guard let owner = fs.engineOwner(paths.mode) else { return .refused("no \(ServiceNames.serviceAccount) account") }
        guard let dir = fs.status(paths.publicFolder) else { return .missing }
        if dir.kind == .symlink { return .refused("the folder is a symlink") }
        if dir.kind != .directory { return .refused("the folder is not a folder") }
        if dir.writableByOthers { return .refused("the folder can be written by others") }
        if dir.ownerUID != owner { return .refused("the folder belongs to someone else") }
        guard let file = fs.status(paths.engineJSON) else { return .missing }
        if file.kind == .symlink { return .refused("engine.json is a symlink") }
        if file.kind != .file { return .refused("engine.json is not a file") }
        if file.writableByOthers { return .refused("engine.json can be written by others") }
        if file.ownerUID != owner { return .refused("engine.json belongs to someone else") }
        guard let data = fs.read(paths.engineJSON) else { return .missing }
        return Self.parse(data)
    }

    public static func parse(_ data: Data) -> EngineLookup {
        guard let info = try? JSON.decode(EngineInfo.self, from: data) else { return .refused("engine.json is not readable JSON") }
        guard let v = info.v else { return .refused("engine.json has no version") }
        if v != EngineInfo.knownVersion { return .tooNew }
        guard info.portNumber != nil else { return .refused("engine.json has no port") }
        guard let tls = info.tls, tls.mode == "local-ca" || tls.mode == "own" else { return .refused("engine.json has no certificate") }
        if tls.mode == "local-ca" && (tls.caPem == nil || tls.caSha256 == nil) { return .refused("engine.json has no CA") }
        if tls.mode == "own" && tls.leafSha256 == nil { return .refused("engine.json has no certificate fingerprint") }
        return .found(info)
    }
}
