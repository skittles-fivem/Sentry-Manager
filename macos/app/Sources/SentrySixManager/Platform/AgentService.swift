import Foundation
import SentryKit
import ServiceManagement

/// "In your login" (3.2): the LaunchAgent in the bundle, registered with
/// SMAppService. When SMAppService refuses (an ad-hoc signed build), the same
/// job is written to ~/Library/LaunchAgents and bootstrapped by hand.
/// Neither needs administrator rights.
@MainActor
struct AgentService {
    enum State: Equatable {
        case enabled, requiresApproval, notRegistered
    }

    let paths = AgentPaths(home: NSHomeDirectory())
    private var service: SMAppService { SMAppService.agent(plistName: ServiceNames.agentPlistName) }
    private var target: String { "\(Launchctl.userDomain)/\(ServiceNames.agentLabel)" }
    private var fallbackInstalled: Bool { FileManager.default.fileExists(atPath: paths.fallbackAgentPlist) }

    var state: State {
        if fallbackInstalled { return .enabled }
        switch service.status {
        case .enabled: return .enabled
        case .requiresApproval: return .requiresApproval
        case .notRegistered, .notFound: return .notRegistered
        @unknown default: return .notRegistered
        }
    }

    func register() async throws {
        do {
            try service.register()
        } catch {
            // an unsigned build (verify on a Mac): the job written by hand
            try await registerFallback()
        }
    }

    func unregister() async {
        try? await service.unregister()
        if fallbackInstalled {
            await Launchctl.run(["bootout", target])
            try? FileManager.default.removeItem(atPath: paths.fallbackAgentPlist)
        }
    }

    /// Stop: TERM, the engine exits 0, so KeepAlive leaves it stopped.
    func stop() async { await Launchctl.run(["kill", "TERM", target]) }
    func start() async { await Launchctl.run(["kickstart", target]) }
    /// A new engine in the bundle (the app was replaced): start it again.
    func restart() async { await Launchctl.run(["kickstart", "-k", target]) }
    func job() async -> LaunchdJob? { await Launchctl.print(target) }

    static func openLoginItems() { SMAppService.openSystemSettingsLoginItems() }

    private func registerFallback() async throws {
        guard let launcher = Bundle.main.url(forAuxiliaryExecutable: "sentry-six-engine")?.path else {
            throw ServiceError("The app is missing its engine launcher. Download it again.")
        }
        let dir = (paths.fallbackAgentPlist as NSString).deletingLastPathComponent
        try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
        try AgentPlist.xml(program: launcher).write(to: URL(fileURLWithPath: paths.fallbackAgentPlist), options: .atomic)
        let r = await Launchctl.run(["bootstrap", Launchctl.userDomain, paths.fallbackAgentPlist])
        if r.status != 0 { throw ServiceError("macOS did not start Sentry Six Manager: \(r.stderr.trimmingCharacters(in: .whitespacesAndNewlines))") }
    }
}
