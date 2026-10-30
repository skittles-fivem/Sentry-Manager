import AppKit
import SentryKit
import SwiftUI

/// The menu bar item's menu (section 4). The status line and the
/// conversions count are SentryKit's MenuBarModel (`service.menu`), fed by
/// the app-wide event stream; the menu never polls /api/stats.
struct MenuBarContent: View {
    @Environment(ServiceController.self) private var service
    @Environment(\.openWindow) private var openWindow
    var needsSetup: Bool
    var criticalUpdateURL: URL?

    var body: some View {
        Text(service.state.text)
        Text(service.menu.conversionsText)
        if let url = criticalUpdateURL {
            Button(MenuBarText.criticalUpdate) { NSWorkspace.shared.open(url) }
        }
        Divider()
        Button(MenuBarText.open) { openMain() }
            .keyboardShortcut("o")
        if service.state == .stopped {
            Button(MenuBarText.start) { Task { await service.start() } }
        } else {
            Button(MenuBarText.stop) {
                if let question = service.menu.stopConfirm, !Self.ask(question) { return }
                Task { await service.stop() }
            }
        }
        if needsSetup {
            Button(MenuBarText.setupCode) { openWindow(id: "setup-code") }
        }
        Button(MenuBarText.logs) {
            // with the option key held: the service log in Finder
            if NSEvent.modifierFlags.contains(.option) {
                let log = EnginePaths(mode: service.mode ?? .daemon, home: NSHomeDirectory()).log
                NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: log)])
            } else {
                openMain()
                NotificationCenter.default.post(name: .showLogs, object: nil)
            }
        }
        Divider()
        Button(MenuBarText.quit) { NSApp.terminate(nil) }
            .keyboardShortcut("q")
    }

    /// A menu cannot hold a sheet: a plain alert of its own.
    private static func ask(_ question: String) -> Bool {
        let alert = NSAlert()
        alert.messageText = question
        alert.addButton(withTitle: MenuBarText.stop)
        alert.addButton(withTitle: "Cancel")
        NSApp.activate()
        return alert.runModal() == .alertFirstButtonReturn
    }

    private func openMain() {
        NSApp.setActivationPolicy(.regular)
        openWindow(id: "main")
        NSApp.activate()
    }
}

extension Notification.Name {
    /// Opens the Overview scrolled to Logs (the skins listen for it).
    static let showLogs = Notification.Name("SentrySixManager.showLogs")
}
