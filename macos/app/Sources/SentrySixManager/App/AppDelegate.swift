import AppKit

/// In the Dock only while a window is open (section 4); the menu bar item
/// keeps the app running when the last window closes.
final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    func applicationDidFinishLaunching(_ notification: Notification) {
        NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: nil, queue: .main) { _ in
            // the closing window still counts; the menu bar's own window does not
            MainActor.assumeIsolated {
                let open = NSApp.windows.filter { $0.isVisible && $0.canBecomeMain }
                if open.count <= 1 { NSApp.setActivationPolicy(.accessory) }
            }
        }
    }
}
