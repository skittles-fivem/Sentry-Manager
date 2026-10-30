import SentryKit
import SwiftUI

/// Settings › This Mac (3.6, 3.7, 2.6): how it runs, the users who sign in
/// without a password (background service), and Uninstall. Each skin hosts it.
struct ThisMacView: View {
    @Environment(ServiceController.self) private var service
    @State private var confirmSwitch = false
    @State private var confirmUninstall = false
    @State private var done = false
    @State private var newUser = ""

    var body: some View {
        Form {
            Section(ServiceText.howItRuns) {
                LabeledContent(ServiceText.howItRuns, value: service.mode.map(ServiceText.modeName) ?? "Not set up")
                Text(service.state.text).foregroundStyle(.secondary)
                Button(ServiceText.change) { confirmSwitch = true }
                    .disabled(service.mode == nil || service.working != nil)
            }
            if service.mode == .daemon {
                Section("Mac users who can sign in without a password") {
                    HStack {
                        TextField("Mac user name", text: $newUser)
                        Button("Add") { Task { await service.localSignIn(allow: true, user: newUser); newUser = "" } }
                            .disabled(!MacUserName.isValid(newUser))
                        Button("Remove") { Task { await service.localSignIn(allow: false, user: newUser); newUser = "" } }
                            .disabled(!MacUserName.isValid(newUser))
                    }
                    Text(ServiceText.adminReason(.localSignIn)).font(.footnote).foregroundStyle(.secondary)
                }
            }
            Section {
                Button(ServiceText.uninstall, role: .destructive) { confirmUninstall = true }
                if done { Text(ServiceText.uninstallDone) }
            }
            if let line = service.working { HStack { ProgressView().controlSize(.small); Text(line) } }
            if let problem = service.problem { Text(verbatim: problem).foregroundStyle(.red) }
        }
        .formStyle(.grouped)
        .confirmationDialog(switchQuestion, isPresented: $confirmSwitch) {
            Button(switchTarget.map(ServiceText.modeName) ?? "") {
                guard let t = switchTarget else { return }
                Task { await service.switchMode(to: t) }
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(ServiceText.adminReason(.switchMode))
        }
        .confirmationDialog(ServiceText.uninstallConfirm, isPresented: $confirmUninstall) {
            Button(ServiceText.keepData) { Task { done = await service.uninstall(removeData: false) } }
            Button(ServiceText.removeAll, role: .destructive) { Task { done = await service.uninstall(removeData: true) } }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(ServiceText.binStays)
        }
    }

    private var switchTarget: ServiceMode? {
        switch service.mode {
        case .daemon?: return .agent
        case .agent?: return .daemon
        case nil: return nil
        }
    }

    private var switchQuestion: String {
        guard let t = switchTarget else { return "" }
        return "Run Sentry Six Manager as \(ServiceText.modeName(t).lowercased()) instead?"
    }
}
