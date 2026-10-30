import Foundation

/// How the engine runs (3.1): a LaunchDaemon as _sentrysix, or a
/// LaunchAgent in the user's login.
public enum ServiceMode: String, Sendable, Codable, CaseIterable {
    case background
    case login
}

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

    public var baseURL: URL? {
        guard let port, port > 0, port < 65536 else { return nil }
        return URL(string: "https://127.0.0.1:\(Int(port))")
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

/// Where the public file, the state and the same-Mac sign-in socket live.
public struct EnginePaths: Sendable, Equatable {
    public static let serviceAccount = "_sentrysix"
    public static let daemonPlist = "/Library/LaunchDaemons/io.github.skittles-fivem.sentry-six-manager.plist"
    public static let agentLabel = "io.github.skittles-fivem.sentry-six-manager.agent"
    public static let serviceCLI = "/Library/SentrySixManager/bin/sentry-six-manager"

    public var mode: ServiceMode
    public var home: String

    public init(mode: ServiceMode, home: String) { self.mode = mode; self.home = home }

    public var publicFolder: String {
        switch mode {
        case .background: return "/Library/Application Support/SentrySixManager Public"
        case .login: return home + "/Library/Application Support/SentrySixManager/public"
        }
    }

    public var engineJSON: String { publicFolder + "/engine.json" }

    /// the setup code file, readable by the app only in login mode
    public var setupCodeFile: String? {
        mode == .login ? home + "/Library/Application Support/SentrySixManager/data/setup-code.txt" : nil
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

    /// the uid the folder and file must belong to
    public func expectedOwner() -> UInt32? {
        paths.mode == .background ? fs.uid(ofUser: EnginePaths.serviceAccount) : fs.currentUID
    }

    public func lookup() -> EngineLookup {
        guard let owner = expectedOwner() else { return .refused("no \(EnginePaths.serviceAccount) account") }
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
        guard info.baseURL != nil else { return .refused("engine.json has no port") }
        guard let tls = info.tls, tls.mode == "local-ca" || tls.mode == "own" else { return .refused("engine.json has no certificate") }
        if tls.mode == "local-ca" && (tls.caPem == nil || tls.caSha256 == nil) { return .refused("engine.json has no CA") }
        if tls.mode == "own" && tls.leafSha256 == nil { return .refused("engine.json has no certificate fingerprint") }
        return .found(info)
    }
}
