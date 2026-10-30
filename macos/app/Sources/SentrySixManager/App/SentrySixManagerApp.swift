import AppKit
import SwiftUI

/// The scenes: the main window (first open asks how it should run, 3.1),
/// the setup-code window and the menu bar item (section 4). The main
/// window's content is the skin root (docs/plans/macos-app.md 5.2); until
/// it lands, a plain status.
struct SentrySixManagerApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @State private var service = ServiceController()

    var body: some Scene {
        // the first scene opens at launch
        Window("Sentry Six Manager", id: "main") {
            Group {
                if service.needsFirstOpen {
                    FirstOpenView()
                } else {
                    MainPlaceholderView()
                }
            }
            .environment(service)
            .onAppear { NSApp.setActivationPolicy(.regular) }
        }
        .windowResizability(.contentSize)

        Window("Setup code", id: "setup-code") {
            SetupCodeView()
                .environment(service)
        }
        .windowResizability(.contentSize)

        MenuBarExtra {
            MenuBarContent(conversions: nil, needsSetup: service.setupCode != nil, criticalUpdateURL: nil)
                .environment(service)
        } label: {
            Image(systemName: "shield")
        }
        .menuBarExtraStyle(.menu)

        Settings {
            ThisMacView()
                .environment(service)
                .frame(minWidth: 480, minHeight: 360)
        }
    }
}

/// Until the skin root is in place: the engine's state and its Start button.
struct MainPlaceholderView: View {
    @Environment(ServiceController.self) private var service

    var body: some View {
        VStack(spacing: 12) {
            Text(service.state.text).font(.title2)
            if service.state == .stopped {
                Button(MenuBarText.start) { Task { await service.start() } }
            }
            if let problem = service.problem { Text(verbatim: problem).foregroundStyle(.red) }
        }
        .padding(40)
        .frame(minWidth: 480, minHeight: 300)
        .task { service.startPolling() }
    }
}
