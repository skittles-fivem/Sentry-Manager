import Foundation

/// The web interface's sentences, word for word (Mac wording). Lines
/// marked native are the app's own, in the same tone.
public enum Strings {
    public enum Shell {
        public static let brandTop = "Sentry Six"
        public static let brandBottom = "Manager"
        public static let appName = "Sentry Six Manager"
        /// native: the web says "Reload the page", which does not apply here
        public static let cantReach = "Can't reach Sentry Six Manager."
        public static let startFromMenuBar = "Start it from the menu bar."
        public static let tookTooLong = "The server took too long to answer."
        public static let signedOut = "Signed out"
        public static let tooLarge = "That is too large to send."
        public static let badAnswer = "Sentry Six Manager sent an answer the app did not understand."
        public static let addFirstVehicle = "+ Add your first vehicle"
        public static let scanning = "scanning…"
        public static let notScanned = "not scanned yet"
        public static let criticalTitle = "CRITICAL UPDATE: INSTALL NOW"
        public static let downloadUpdate = "Download update"
        public static let signOut = "Sign Out"
        public static let starting = "Starting…"
        public static let stopped = "Stopped"

        public static func windowTitle(_ page: String) -> String { "\(page) · Sentry Six Manager" }
    }

    public enum Confirm {
        public static let title = "Confirm with your password"
        public static let body = "This change affects where footage comes from or what code runs, so Sentry Six Manager asks for your password again."
        public static let retry = "That password was not right. Try again."
        public static let field = "Password"
        public static let cancel = "Cancel"
        public static let confirm = "Confirm"
        public static let notConfirmed = "Not changed: the password was not confirmed."
    }

    public enum SignIn {
        public static let title = "Sign in"
        public static let lead = "Use the account for this Sentry Six Manager."
        public static let userName = "User name"
        public static let password = "Password"
        public static let button = "Sign in"
        public static let couldNotReach = "Could not reach Sentry Six Manager."
        public static let createTitle = "Create your account"
        public static let createLead = "This is the first start. Enter the setup code (in Terminal on the Mac, run sudo sentry-six-manager setup-code), then choose the user name and password that protect this Sentry Six Manager."
        public static let confirmPassword = "Confirm password"
        public static let setupCode = "Setup code"
        public static let setupCodePlaceholder = "XXXXX-XXXXX-XXXXX"
        public static let setupCodeHint = "In Terminal on the Mac, run sudo sentry-six-manager setup-code"
        /// native
        public static let getSetupCode = "Get the setup code"
        public static let createButton = "Create account"
        public static let mismatch = "The passwords do not match."
        public static let tooShort = "Use a password of at least 8 characters."
        /// native: runs same-Mac sign-in again (2.6)
        public static let signInOnThisMac = "Sign in on this Mac"
        /// native: /api/auth/me said secure:false
        public static let insecure = "This Sentry Six Manager does not use HTTPS, so the app will not send a password to it. Turn HTTPS back on (TLS_MODE) and open the app again."

        public static func fingerprint(_ fp: String) -> String { "This server's certificate (SHA-256): \(fp)" }
    }

    public enum Pinning {
        public static let title = "This isn't the certificate Sentry Six Manager made. Don't sign in."
        public static func expected(_ fp: String) -> String { "Expected: \(fp)" }
        public static func presented(_ fp: String) -> String { "This server: \(fp)" }
        /// native: engine.json has a version this app does not know
        public static let appTooOld = "This app is older than the Sentry Six Manager it talks to. Get the new app from the release page."
    }

    public enum Pack {
        /// native (5.4)
        public static let title = "This server uses an interface the app can't show"
        public static func body(_ name: String) -> String { "\"\(name)\" was uploaded to this server. It is a web page, so it opens in your browser." }
        public static let openInBrowser = "Open in Browser"
        public static let useOriginal = "Use the Original Interface Here"
        public static let interfaceSettings = "Interface Settings…"
        public static let originalMenu = "Use the Original Interface in This App"
    }
}
