// The app target's entry point. The windows, menu bar and skins come in
// later units; this keeps the target buildable on a Mac. Not built on Linux.

import SwiftUI
import SentryKit

@main
struct SentrySixManagerApp: App {
    var body: some Scene {
        WindowGroup("Sentry Six Manager") {
            Text(verbatim: "Sentry Six Manager")
                .frame(minWidth: 390, minHeight: 600)
        }
    }
}
