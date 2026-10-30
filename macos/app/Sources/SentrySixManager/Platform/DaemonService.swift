import Foundation
import SentryKit

/// The background service (3.4): install.sh through one administrator
/// prompt, then the root-owned CLI for everything else.
struct DaemonService {
    private var target: String { "system/\(ServiceNames.daemonLabel)" }

    /// Installed here, by this app or by the Terminal install.
    var installed: Bool {
        FileManager.default.fileExists(atPath: DaemonPaths.plist) && FileManager.default.fileExists(atPath: DaemonPaths.program)
    }

    var engineFolder: String? { Bundle.main.resourceURL?.appendingPathComponent("engine").path }

    func job() async -> LaunchdJob? { await Launchctl.print(target) }

    /// Install, or update, from the app's own engine. Returns the setup code
    /// when install.sh printed one (kept in memory only).
    func install(update: Bool = false, importData: String? = nil) async throws -> String? {
        guard let engine = engineFolder else { throw ServiceError(InstallScript.noEngineMessage) }
        let script = try InstallScript.installDaemon(engine: engine, manifestSHA256: EngineManifest.sha256, localUser: NSUserName(), importData: importData, update: update)
        let out = try await admin(script, update ? .update : .install)
        return InstallScript.setupCode(fromOutput: out)
    }

    func start() async throws { _ = try await admin(InstallScript.start(), .start) }
    func stop() async throws { _ = try await admin(InstallScript.stop(), .stop) }

    func setupCode() async throws -> String? {
        InstallScript.setupCode(fromOutput: try await admin(InstallScript.setupCode(), .setupCode))
    }

    func allowFolder(_ path: String, write: Bool) async throws {
        _ = try await admin(try InstallScript.allowFolder(path, write: write), .allowFolder)
    }

    func localSignIn(allow: Bool, user: String) async throws {
        _ = try await admin(try InstallScript.localSignIn(allow: allow, user: user), .localSignIn)
    }

    /// Background service -> in your login: stops it and hands the data back once.
    func handBack() async throws {
        _ = try await admin(try InstallScript.handBackToUser(user: NSUserName(), home: NSHomeDirectory()), .switchMode)
    }

    func uninstall(removeData: Bool) async throws {
        _ = try await admin(InstallScript.uninstallDaemon(removeData: removeData), .uninstall)
    }

    private func admin(_ script: String, _ action: ServiceText.AdminAction) async throws -> String {
        switch await AdminRunner.run(script, action: action) {
        case .ok(let out): return out
        case .cancelled: throw CancellationError()
        case .failed(let message): throw ServiceError(message)
        }
    }
}
