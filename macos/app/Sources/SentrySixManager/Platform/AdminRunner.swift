import Foundation

/// Runs one step with administrator rights (3.4): `do shell script ... with
/// administrator privileges`, one standard macOS password window. It runs in
/// osascript, off the main thread, so the window stays responsive during a
/// minute-long install; `with prompt` puts the app's own reason in the
/// password window (verify on a Mac).
enum AdminRunner {
    static func run(_ script: String, action: ServiceText.AdminAction) async -> InstallScript.AdminOutcome {
        let source = InstallScript.appleScript(script, prompt: "Sentry Six Manager wants to make changes. " + ServiceText.adminReason(action))
        let r = await Command.run("/usr/bin/osascript", ["-e", source])
        return InstallScript.adminOutcome(status: r.status, stdout: r.stdout, stderr: r.stderr)
    }
}
