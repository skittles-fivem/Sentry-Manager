import AppKit
import SentryKit
import SwiftUI

/// The small window at first open (3.1): how should it run?
struct FirstOpenView: View {
    @Environment(ServiceController.self) private var service
    @State private var choice: ServiceMode = .daemon

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text(ServiceText.firstOpenTitle).font(.title2).bold()
            if service.daemonAlreadyInstalled {
                Text(ServiceText.alreadyInstalled)
            } else {
                Picker("", selection: $choice) {
                    option(ServiceText.daemonTitle, ServiceText.daemonBody).tag(ServiceMode.daemon)
                    option(ServiceText.agentTitle, ServiceText.agentBody).tag(ServiceMode.agent)
                }
                .pickerStyle(.radioGroup)
                .labelsHidden()
                Text(ServiceText.changeLater).font(.footnote).foregroundStyle(.secondary)
            }
            if let line = service.working {
                HStack { ProgressView().controlSize(.small); Text(line) }
            } else if choice == .daemon && !service.daemonAlreadyInstalled {
                Text(ServiceText.adminReason(.install)).font(.footnote).foregroundStyle(.secondary)
            }
            if let problem = service.problem {
                Text(verbatim: problem).foregroundStyle(.red)
            }
            if service.agentNeedsApproval {
                Text(ServiceText.loginItemsApproval)
                Button("Open Login Items") { AgentService.openLoginItems() }
            }
            HStack {
                Spacer()
                Button(ServiceText.quitButton) { NSApp.terminate(nil) }
                Button(ServiceText.continueButton) {
                    // once chosen, the same window shows the app
                    Task { await service.choose(service.daemonAlreadyInstalled ? .daemon : choice) }
                }
                .keyboardShortcut(.defaultAction)
                .disabled(service.working != nil)
            }
        }
        .padding(24)
        .frame(width: 480)
    }

    private func option(_ title: String, _ body: String) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title).bold()
            Text(body).font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
        .padding(.vertical, 4)
    }
}

/// The setup code in a small window with a Copy button (section 4, item 7).
struct SetupCodeView: View {
    @Environment(ServiceController.self) private var service

    var body: some View {
        VStack(spacing: 12) {
            if let code = service.setupCode {
                Text(verbatim: code).font(.system(.title, design: .monospaced)).textSelection(.enabled)
                Button("Copy") {
                    NSPasteboard.general.clearContents()
                    NSPasteboard.general.setString(code, forType: .string)
                }
            } else {
                Text("No setup code: the account is already set up.")
            }
            if let problem = service.problem { Text(verbatim: problem).foregroundStyle(.red) }
        }
        .padding(24)
        .frame(minWidth: 320)
        .task { if service.setupCode == nil { await service.fetchSetupCode() } }
    }
}
