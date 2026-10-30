import Foundation
import IOKit.ps
import Observation
import Security
import SentryKit

/// The app's one view of the engine's service: which mode, whether it runs,
/// and the actions on it (first open, Start/Stop, switching, uninstall).
/// The menu bar and Settings › This Mac both read it.
@MainActor
@Observable
final class ServiceController {
    private(set) var mode: ServiceMode?
    /// The menu bar's data: the status line and the conversions count.
    let menu = MenuBarModel()
    var state: EngineState { menu.state }
    private(set) var agentNeedsApproval = false
    /// The plain line under a step that failed, as the step said it.
    var problem: String?
    /// Busy with an install, a switch or an uninstall.
    private(set) var working: String?
    /// From install.sh's output, the socket or the file; memory only.
    private(set) var setupCode: String?
    private(set) var engine: EngineInfo?

    private let agent = AgentService()
    private let daemon = DaemonService()
    private var polling: Task<Void, Never>?

    init() {
        mode = Self.installedMode(recorded: UserDefaults.standard.string(forKey: ServiceMode.defaultsKey).flatMap(ServiceMode.init(rawValue:)))
    }

    /// The recorded choice, checked against what is really installed (2.3 step 1).
    private static func installedMode(recorded: ServiceMode?) -> ServiceMode? {
        let daemonThere = DaemonService().installed
        switch recorded {
        case .daemon? where daemonThere: return .daemon
        case .agent?: return .agent
        default: return daemonThere ? .daemon : nil
        }
    }

    var needsFirstOpen: Bool { mode == nil }
    var daemonAlreadyInstalled: Bool { daemon.installed }

    private var engineJSONPath: String {
        EnginePaths(mode: mode ?? .daemon, home: NSHomeDirectory()).engineJSON
    }

    // ---- first open (3.1)

    func choose(_ choice: ServiceMode) async {
        problem = nil
        working = ServiceText.starting
        defer { working = nil }
        do {
            switch choice {
            case .daemon:
                if !daemon.installed { setupCode = try await daemon.install() }
            case .agent:
                try await agent.register()
                agentNeedsApproval = agent.state == .requiresApproval
                setupCode = readOwnSetupCode()
            }
            record(choice)
            menu.didStart(at: Date())
            startPolling()
            await waitUntilRunning(seconds: 60)
        } catch is CancellationError {
            // the password window was cancelled: nothing changed
        } catch {
            problem = String(describing: error)
        }
    }

    private func record(_ m: ServiceMode) {
        mode = m
        UserDefaults.standard.set(m.rawValue, forKey: ServiceMode.defaultsKey)
    }

    /// "In your login": the code is in the user's own data folder.
    private func readOwnSetupCode() -> String? {
        guard let file = EnginePaths(mode: .agent, home: NSHomeDirectory()).setupCodeFile,
              let text = try? String(contentsOfFile: file, encoding: .utf8) else { return nil }
        let code = text.trimmingCharacters(in: .whitespacesAndNewlines)
        return LocalSignIn.isSetupCode(code) ? code : nil
    }

    /// The menu bar's Setup code…: the file in login mode, the admin prompt for the service.
    func fetchSetupCode() async {
        if mode == .agent { setupCode = readOwnSetupCode(); return }
        do { setupCode = try await daemon.setupCode() } catch is CancellationError {} catch { problem = String(describing: error) }
    }

    // ---- start and stop (section 4)

    func start() async {
        problem = nil
        do {
            switch mode {
            case .agent?: await agent.start()
            case .daemon?: try await daemon.start()
            case nil: return
            }
            menu.didStart(at: Date())
            await refresh()
        } catch is CancellationError {} catch { problem = String(describing: error) }
    }

    func stop() async {
        problem = nil
        do {
            switch mode {
            case .agent?: await agent.stop()
            case .daemon?: try await daemon.stop()
            case nil: return
            }
            await refresh()
        } catch is CancellationError {} catch { problem = String(describing: error) }
    }

    // ---- switching (3.6)

    func switchMode(to target: ServiceMode) async {
        guard let from = mode, from != target else { return }
        problem = nil
        working = ServiceText.starting
        defer { working = nil }
        do {
            if target == .daemon {
                let own = AgentPaths(home: NSHomeDirectory()).data
                await agent.stop()
                // the agent lets go of the port and the data before the service starts
                for _ in 0..<60 {
                    guard await agent.job()?.running == true else { break }
                    try? await Task.sleep(for: .seconds(1))
                }
                await agent.unregister()
                let importData = FileManager.default.fileExists(atPath: own) ? own : nil
                setupCode = try await daemon.install(update: daemon.installed, importData: importData)
            } else {
                try await daemon.handBack()
                try await agent.register()
                agentNeedsApproval = agent.state == .requiresApproval
            }
            record(target)
            menu.didStart(at: Date())
            await waitUntilRunning(seconds: 60)
        } catch is CancellationError {} catch { problem = String(describing: error) }
    }

    /// The app's engine is newer than the service's (3.5).
    func updateService() async {
        working = ServiceText.starting
        defer { working = nil }
        do {
            _ = try await daemon.install(update: true)
            menu.didStart(at: Date())
            await waitUntilRunning(seconds: 60)
        } catch is CancellationError {} catch { problem = String(describing: error) }
    }

    func allowFolder(_ path: String, write: Bool) async {
        do { try await daemon.allowFolder(path, write: write) } catch is CancellationError {} catch { problem = String(describing: error) }
    }

    func localSignIn(allow: Bool, user: String) async {
        do { try await daemon.localSignIn(allow: allow, user: user) } catch is CancellationError {} catch { problem = String(describing: error) }
    }

    // ---- uninstall (3.7)

    /// true when it is done (the app then says to drag it to the Bin).
    func uninstall(removeData: Bool) async -> Bool {
        problem = nil
        do {
            switch mode {
            case .daemon?:
                try await daemon.uninstall(removeData: removeData)
            case .agent?, nil:
                await agent.stop()
                await agent.unregister()
                let p = AgentPaths(home: NSHomeDirectory())
                if removeData { try? FileManager.default.removeItem(atPath: p.state) }
                try? FileManager.default.removeItem(atPath: p.logDir)
            }
        } catch is CancellationError {
            return false
        } catch {
            problem = String(describing: error)
            return false
        }
        // the stored session and the app's settings
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: SessionCookie.keychainService]
        SecItemDelete(query as CFDictionary)
        UserDefaults.standard.removePersistentDomain(forName: ServiceNames.bundleID)
        mode = nil
        polling?.cancel()
        return true
    }

    // ---- status (section 4: /healthz every 10 s, 15 s on battery, and launchd)

    func startPolling() {
        polling?.cancel()
        polling = Task { [weak self] in
            while !Task.isCancelled {
                await self?.refresh()
                try? await Task.sleep(for: .seconds(EngineStateTracker.pollInterval(onBattery: Self.onBattery)))
            }
        }
    }

    func refresh() async {
        engine = (try? Data(contentsOf: URL(fileURLWithPath: engineJSONPath))).flatMap(EngineInfo.decode)
        let job: LaunchdJob?
        switch mode {
        case .agent?: job = await agent.job()
        case .daemon?: job = await daemon.job()
        case nil: job = nil
        }
        if mode == .agent { agentNeedsApproval = agent.state == .requiresApproval }
        let ok = await Self.healthz(port: engine?.portNumber ?? ServiceNames.defaultPort)
        menu.health(ok: ok, launchdRunning: job?.running, now: Date())
    }

    nonisolated static var onBattery: Bool {
        guard let blob = IOPSCopyPowerSourcesInfo()?.takeRetainedValue(),
              let type = IOPSGetProvidingPowerSourceType(blob)?.takeUnretainedValue() else { return false }
        return (type as String) == kIOPMBatteryPowerKey
    }

    private func waitUntilRunning(seconds: Int) async {
        for _ in 0..<seconds {
            await refresh()
            if state == .running { return }
            try? await Task.sleep(for: .seconds(1))
        }
    }

    /// /healthz answers on plain HTTP as well (install.sh asks it the same
    /// way); it carries no secret. The signed-in API goes over pinned HTTPS.
    nonisolated static func healthz(port: Int) async -> Bool {
        guard let url = URL(string: "http://127.0.0.1:\(port)/healthz") else { return false }
        var req = URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 3)
        req.httpShouldHandleCookies = false
        guard let (data, res) = try? await URLSession.shared.data(for: req) else { return false }
        return (res as? HTTPURLResponse)?.statusCode == 200 && String(decoding: data, as: UTF8.self) == "ok"
    }
}
