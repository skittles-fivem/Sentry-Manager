import Foundation
import XCTest
@testable import SentryKit

final class AgentEnvironmentTests: XCTestCase {
    let engine = "/Applications/Sentry Six Manager.app/Contents/Resources/engine"

    func testPathsFromTheHomeFolder() {
        let p = AgentPaths(home: "/Users/anna/")
        XCTAssertEqual(p.state, "/Users/anna/Library/Application Support/SentrySixManager")
        XCTAssertEqual(p.data, "\(p.state)/data")
        XCTAssertEqual(p.engineJSON, "\(p.state)/public/engine.json")
        XCTAssertEqual(p.setupCode, "\(p.state)/data/setup-code.txt")
        XCTAssertEqual(p.log, "/Users/anna/Library/Logs/SentrySixManager/service.log")
        XCTAssertEqual(p.fallbackAgentPlist, "/Users/anna/Library/LaunchAgents/io.github.skittles-fivem.sentry-six-manager.agent.plist")
        // every folder 0700, public/ 0755
        for f in p.folders { XCTAssertEqual(f.mode, f.path == p.publicDir ? 0o755 : 0o700, f.path) }
    }

    func testTheDaemonPlistKeysPlusTheAgentOnes() {
        let a = AgentEnvironment(home: "/Users/anna", engine: engine + "/", uid: 501)
        let state = "/Users/anna/Library/Application Support/SentrySixManager"
        XCTAssertEqual(a.environment, [
            "NODE_ENV": "production",
            "APP_ROOT": "\(engine)/app",
            "DATA_PATH": "\(state)/data",
            "RUN_DIR": "\(state)/run",
            "FOOTAGE_ROOT": "\(state)/footage",
            "FFMPEG_PATH": "\(engine)/ffmpeg/ffmpeg",
            "FFPROBE_PATH": "\(engine)/ffmpeg/ffprobe",
            "SENTRY_NOREPLACE_RENAME": "\(engine)/bin/sentry-fsops",
            "HOME": "\(state)/run/home",
            "TMPDIR": "\(state)/run/tmp",
            "PATH": "/usr/bin:/bin:/usr/sbin:/sbin",
            "UV_THREADPOOL_SIZE": "16",
            "SENTRY_CONSOLE": "off",
            "PUBLIC_INFO_PATH": "\(state)/public/engine.json",
            "LOCAL_SIGNIN_SOCKET": "\(state)/run/signin.sock",
            "SERVICE_MODE": "agent",
        ])
        XCTAssertEqual(a.arguments, [
            "\(engine)/node/bin/node", "--disable-warning=ExperimentalWarning",
            "--env-file-if-exists=\(state)/manager.env", "\(engine)/app/server/index.js",
        ])
        XCTAssertFalse(a.socketMoved)
        XCTAssertEqual(a.envp.first, "APP_ROOT=\(engine)/app")
        XCTAssertFalse(a.envp.contains { $0.hasPrefix("DYLD_") })
    }

    func testALongHomeFolderMovesTheSocketToPrivateTmp() {
        let tail = "/Library/Application Support/SentrySixManager/run/signin.sock"
        let fits = "/Users/" + String(repeating: "a", count: 103 - 7 - tail.utf8.count)
        XCTAssertEqual(AgentPaths(home: fits).socket(uid: 501).utf8.count, 103)
        XCTAssertEqual(AgentPaths(home: fits).socket(uid: 501), "\(AgentPaths(home: fits).run)/signin.sock")
        let long = fits + "b"
        XCTAssertEqual(AgentPaths(home: long).socket(uid: 501), "/private/tmp/sentrysix-501/signin.sock")
        let a = AgentEnvironment(home: long, engine: engine, uid: 501)
        XCTAssertTrue(a.socketMoved)
        XCTAssertEqual(a.environment["LOCAL_SIGNIN_SOCKET"], "/private/tmp/sentrysix-501/signin.sock")
        // bytes, not characters
        XCTAssertFalse(SocketPath.fits(String(repeating: "é", count: 52)))
        XCTAssertTrue(SocketPath.fits(String(repeating: "é", count: 51)))
    }

    func testTheShortSocketFolderMustBeTheUsersOwn0700Folder() {
        XCTAssertTrue(SocketFolderCheck.acceptable(isDirectory: true, isSymlink: false, owner: 501, mode: 0o700, uid: 501))
        XCTAssertFalse(SocketFolderCheck.acceptable(isDirectory: true, isSymlink: false, owner: 502, mode: 0o700, uid: 501))
        XCTAssertFalse(SocketFolderCheck.acceptable(isDirectory: true, isSymlink: false, owner: 501, mode: 0o755, uid: 501))
        XCTAssertFalse(SocketFolderCheck.acceptable(isDirectory: false, isSymlink: true, owner: 501, mode: 0o700, uid: 501))
        XCTAssertFalse(SocketFolderCheck.acceptable(isDirectory: false, isSymlink: false, owner: 501, mode: 0o700, uid: 501))
    }

    func testTheEngineFolderFromTheLaunchersPath() {
        XCTAssertEqual(AgentEnvironment.engineFolder(launcherPath: "/Applications/Sentry Six Manager.app/Contents/MacOS/sentry-six-engine"), engine)
        XCTAssertNil(AgentEnvironment.engineFolder(launcherPath: "/usr/local/bin/sentry-six-engine"))
        XCTAssertNil(AgentEnvironment.engineFolder(launcherPath: "sentry-six-engine"))
    }
}

final class LogRotationTests: XCTestCase {
    func testThePlan() {
        XCTAssertTrue(LogRotation.plan(log: "/l", size: LogRotation.maxBytes - 1).isEmpty)
        let moves = LogRotation.plan(log: "/l", size: LogRotation.maxBytes).map { "\($0.from)>\($0.to)" }
        XCTAssertEqual(moves, ["/l.2>/l.3", "/l.1>/l.2", "/l>/l.1"])
    }

    func testRotatesAtTheLimitKeepingThree() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("ssm-rot-\(UUID().uuidString)").path
        try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(atPath: dir) }
        let log = "\(dir)/service.log"
        for n in 1...3 { try "old\(n)".write(toFile: "\(log).\(n)", atomically: false, encoding: .utf8) }
        try Data(count: Int(LogRotation.maxBytes)).write(to: URL(fileURLWithPath: log))
        LogRotation.rotate(log: log)
        XCTAssertFalse(FileManager.default.fileExists(atPath: log))
        XCTAssertEqual(try String(contentsOfFile: "\(log).2", encoding: .utf8), "old1")
        XCTAssertEqual(try String(contentsOfFile: "\(log).3", encoding: .utf8), "old2")
        XCTAssertEqual(try FileManager.default.attributesOfItem(atPath: "\(log).1")[.size] as? NSNumber, NSNumber(value: LogRotation.maxBytes))
        // a small log stays
        try "x".write(toFile: log, atomically: false, encoding: .utf8)
        LogRotation.rotate(log: log)
        XCTAssertTrue(FileManager.default.fileExists(atPath: log))
    }
}

final class InstallScriptTests: XCTestCase {
    let sha = String(repeating: "ab", count: 32)

    func testQuoting() {
        XCTAssertEqual(ShellQuote.word("plain"), "'plain'")
        XCTAssertEqual(ShellQuote.word("it's $HOME `x` \"y\""), "'it'\\''s $HOME `x` \"y\"'")
        XCTAssertFalse(ShellQuote.isSafe("a\nb"))
        XCTAssertFalse(ShellQuote.isSafe("a\u{0}b"))
        XCTAssertFalse(ShellQuote.isSafe(""))
        XCTAssertEqual(InstallScript.appleScriptLiteral("a\"b\\c"), "\"a\\\"b\\\\c\"")
    }

    func testUserNames() {
        for ok in ["anna", "anna.b", "a_b-c", "_svc", "A1"] { XCTAssertTrue(MacUserName.isValid(ok), ok) }
        for bad in ["", "-x", ".x", "a b", "a'b", "a/b", "ä", "a;b"] { XCTAssertFalse(MacUserName.isValid(bad), bad) }
    }

    func testTheInstallStepsInOrder() throws {
        let s = try InstallScript.installDaemon(engine: "/Applications/It's $here.app/Contents/Resources/engine", manifestSHA256: sha, localUser: "anna")
        let order = ["umask 077", "mktemp -d /Library/SentrySixManager.stage.", "/usr/bin/ditto '/Applications/It'\\''s $here.app/Contents/Resources/engine' \"$S\"",
                     "find . -type l", "shasum -a 256 MANIFEST.sha256", "! -path ./MANIFEST.sha256", "shasum -a 256 -c MANIFEST.sha256", "umask 022",
                     "/bin/bash \"$S/install.sh\" --from-dir \"$S\" --service-mode daemon --local-user 'anna'"]
        var at = s.startIndex
        for step in order {
            guard let r = s.range(of: step, range: at..<s.endIndex) else { return XCTFail("missing or out of order: \(step)\n\(s)") }
            at = r.upperBound
        }
        XCTAssertTrue(s.contains("= '\(sha)' ]"))
        XCTAssertFalse(s.contains("--update"))
        XCTAssertFalse(s.contains("--import-data"))
        let u = try InstallScript.installDaemon(engine: "/e", manifestSHA256: sha, localUser: "anna", importData: "/Users/anna/Library/Application Support/SentrySixManager/data", update: true)
        XCTAssertTrue(u.hasSuffix("--local-user 'anna' --import-data '/Users/anna/Library/Application Support/SentrySixManager/data' --update"))
    }

    func testRefusedBeforeAnyPrompt() {
        XCTAssertThrowsError(try InstallScript.installDaemon(engine: "/e", manifestSHA256: nil, localUser: "anna")) {
            XCTAssertEqual(($0 as? ServiceError)?.message, InstallScript.noEngineMessage)
        }
        XCTAssertThrowsError(try InstallScript.installDaemon(engine: "/e", manifestSHA256: sha.uppercased(), localUser: "anna"))
        XCTAssertThrowsError(try InstallScript.installDaemon(engine: "/e", manifestSHA256: "abc", localUser: "anna"))
        XCTAssertThrowsError(try InstallScript.installDaemon(engine: "/e", manifestSHA256: sha, localUser: "a'b"))
        XCTAssertThrowsError(try InstallScript.installDaemon(engine: "/e\nrm -rf /", manifestSHA256: sha, localUser: "anna"))
        XCTAssertThrowsError(try InstallScript.allowFolder("/a\nb"))
        XCTAssertThrowsError(try InstallScript.localSignIn(allow: true, user: "x y"))
    }

    func testTheOtherAdminSteps() throws {
        XCTAssertEqual(InstallScript.start(), "'/Library/SentrySixManager/bin/sentry-six-manager' 'start'")
        XCTAssertEqual(InstallScript.stop(), "'/Library/SentrySixManager/bin/sentry-six-manager' 'stop'")
        XCTAssertEqual(InstallScript.setupCode(), "'/Library/SentrySixManager/bin/sentry-six-manager' 'setup-code'")
        XCTAssertEqual(try InstallScript.allowFolder("/Volumes/TESLA DRIVE's", write: true),
                       "'/Library/SentrySixManager/bin/sentry-six-manager' 'allow-folder' '/Volumes/TESLA DRIVE'\\''s' '--write'")
        XCTAssertEqual(try InstallScript.localSignIn(allow: false, user: "bob"), "'/Library/SentrySixManager/bin/sentry-six-manager' 'local-signin' 'remove' 'bob'")
        XCTAssertEqual(InstallScript.uninstallDaemon(removeData: true), "'/bin/bash' '/Library/SentrySixManager/uninstall.sh' '--remove-data'")
        XCTAssertEqual(InstallScript.uninstallDaemon(removeData: false), "'/bin/bash' '/Library/SentrySixManager/uninstall.sh'")
        let a = InstallScript.appleScript("echo 'hi' \"there\"", prompt: "Your \"password\".")
        XCTAssertTrue(a.hasPrefix("do shell script \"/bin/bash -c 'echo '\\\\''hi'\\\\'' \\\"there\\\"'\" with prompt "), a)
        XCTAssertTrue(a.hasSuffix(" with prompt \"Your \\\"password\\\".\" with administrator privileges without altering line endings"), a)
    }

    func testWhatTheAdminStepAnswered() {
        XCTAssertEqual(InstallScript.adminOutcome(status: 0, stdout: "SETUP-CODE: X\n", stderr: ""), .ok("SETUP-CODE: X\n"))
        XCTAssertEqual(InstallScript.adminOutcome(status: 1, stdout: "", stderr: "0:180: execution error: User canceled. (-128)\n"), .cancelled)
        XCTAssertEqual(InstallScript.adminOutcome(status: 1, stdout: "", stderr: "0:180: execution error: install.sh: run it as root (1)\n"), .failed("install.sh: run it as root"))
        XCTAssertEqual(InstallScript.adminOutcome(status: 1, stdout: "", stderr: ""), .failed("The step did not finish."))
    }

    func testEveryStepParsesWithBash() throws {
        let scripts = [
            try InstallScript.installDaemon(engine: "/A b/c'd", manifestSHA256: sha, localUser: "anna", importData: "/x y", update: true),
            try InstallScript.handBackToUser(user: "anna", home: "/Users/anna"),
            InstallScript.start(), InstallScript.uninstallDaemon(removeData: true),
        ]
        for s in scripts { XCTAssertEqual(try bash(["-n", "-c", s]).status, 0, s) }
    }

    func testHandBackCopiesAsTheUserOnlyWhenTheUserHasNoData() throws {
        let s = try InstallScript.handBackToUser(user: "anna", home: "/Users/anna")
        XCTAssertTrue(s.contains("'/Library/SentrySixManager/bin/sentry-six-manager' stop"))
        XCTAssertTrue(s.contains("/bin/launchctl bootout 'system/io.github.skittles-fivem.sentry-six-manager'"))
        XCTAssertTrue(s.contains("/bin/launchctl disable 'system/io.github.skittles-fivem.sentry-six-manager'"))
        XCTAssertTrue(s.contains("if [ ! -e \"$D\" ]"))
        XCTAssertTrue(s.contains("/usr/bin/sudo -u 'anna' /usr/bin/ditto \"$T/data\" \"$D\""))
        XCTAssertThrowsError(try InstallScript.handBackToUser(user: "anna", home: "relative"))
    }

    /// The manifest check itself, run with bash on a real folder (the macOS
    /// tools swapped for their Linux equivalents, install.sh for an echo).
    func testTheManifestCheckRuns() throws {
        guard FileManager.default.isExecutableFile(atPath: "/usr/bin/shasum") else { throw XCTSkip("needs shasum") }
        let fm = FileManager.default
        let base = fm.temporaryDirectory.appendingPathComponent("ssm-manifest-\(UUID().uuidString)").path
        defer { try? fm.removeItem(atPath: base) }
        let engine = "\(base)/engine"
        try fm.createDirectory(atPath: "\(engine)/app/server", withIntermediateDirectories: true)
        try "// app\n".write(toFile: "\(engine)/app/server/index.js", atomically: false, encoding: .utf8)
        try "echo installed \"$@\"\n".write(toFile: "\(engine)/install.sh", atomically: false, encoding: .utf8)
        // the manifest as build-app.sh writes it
        let manifest = try bash(["-c", "cd \(ShellQuote.word(engine)) && /usr/bin/find . -type f ! -path ./MANIFEST.sha256 | LC_ALL=C /usr/bin/sort | while IFS= read -r f; do /usr/bin/shasum -a 256 \"$f\"; done > MANIFEST.sha256 && /usr/bin/shasum -a 256 MANIFEST.sha256"])
        let sha = String(manifest.out.prefix(64))
        func run(_ sha: String) throws -> (status: Int32, out: String) {
            var s = try InstallScript.installDaemon(engine: engine, manifestSHA256: sha, localUser: "anna")
            s = s.replacingOccurrences(of: "/usr/bin/mktemp -d /Library/SentrySixManager.stage.XXXXXX", with: "mktemp -d \(ShellQuote.word(base))/stage.XXXXXX")
                .replacingOccurrences(of: "/usr/bin/ditto ", with: "cp -R -T ")
            return try bash(["-c", s])
        }
        let ok = try run(sha)
        XCTAssertEqual(ok.status, 0, ok.out)
        XCTAssertTrue(ok.out.contains("installed --from-dir"), ok.out)
        // another manifest than the one compiled in
        XCTAssertEqual(try run(String(repeating: "0", count: 64)).out.trimmingCharacters(in: .whitespacesAndNewlines), InstallScript.changedMessage)
        // a file added to the bundle that the manifest does not list
        try "x".write(toFile: "\(engine)/app/server/extra.js", atomically: false, encoding: .utf8)
        XCTAssertEqual(try run(sha).status, 65)
        try fm.removeItem(atPath: "\(engine)/app/server/extra.js")
        // a listed file changed
        try "// changed\n".write(toFile: "\(engine)/app/server/index.js", atomically: false, encoding: .utf8)
        XCTAssertEqual(try run(sha).status, 65)
        // the staging folder never stays
        XCTAssertEqual(try fm.contentsOfDirectory(atPath: base).filter { $0.hasPrefix("stage.") }, [])
    }

    func testSetupCodeFromTheOutput() {
        let out = "Sentry Six Manager is running.\n  Setup code: ABCDE-FGHIJ-KLMNO\nSETUP-CODE: ABCDE-FGHIJ-KLMNO\n"
        XCTAssertEqual(InstallScript.setupCode(fromOutput: out), "ABCDE-FGHIJ-KLMNO")
        XCTAssertEqual(InstallScript.setupCode(fromOutput: "Setup code: WXYZ1-23456-ABCDE\r"), "WXYZ1-23456-ABCDE")
        XCTAssertNil(InstallScript.setupCode(fromOutput: "No setup code: the account is already set up."))
        XCTAssertNil(InstallScript.setupCode(fromOutput: "SETUP-CODE: short"))
        XCTAssertNil(InstallScript.setupCode(fromOutput: "SETUP-CODE: ABCDE-FGHIJ-KLMN!"))
    }

    private func bash(_ args: [String]) throws -> (status: Int32, out: String) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/bash")
        p.arguments = args
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = pipe
        try p.run()
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        return (p.terminationStatus, String(decoding: data, as: UTF8.self))
    }
}

final class ServiceStatusTests: XCTestCase {
    func testLaunchctlPrint() {
        let text = """
        system/io.github.skittles-fivem.sentry-six-manager = {
        \tactive count = 1
        \tstate = running
        \tpid = 4242
        \tlast exit code = 0
        \tendpoints = {
        \t\tstate = waiting
        \t}
        }
        """
        XCTAssertEqual(LaunchdJob.parse(text), LaunchdJob(running: true, pid: 4242, lastExitCode: 0))
        XCTAssertEqual(LaunchdJob.parse("x = {\n\tstate = not running\n\tlast exit code = 78: EX_CONFIG\n}"), LaunchdJob(running: false, pid: nil, lastExitCode: 78))
        XCTAssertNil(LaunchdJob.parse("Could not find service \"x\" in domain for system"))
    }

    func testTheStatusLine() {
        var t = EngineStateTracker()
        let t0 = Date(timeIntervalSince1970: 1_000)
        t.didStart(at: t0)
        XCTAssertEqual(t.update(healthOK: false, launchdRunning: true, now: t0.addingTimeInterval(30)), .starting)
        XCTAssertEqual(t.update(healthOK: false, launchdRunning: false, now: t0.addingTimeInterval(59)), .starting)
        XCTAssertEqual(t.update(healthOK: true, launchdRunning: true, now: t0.addingTimeInterval(61)), .running)
        // three failures in a row while launchd says running
        XCTAssertEqual(t.update(healthOK: false, launchdRunning: true, now: t0.addingTimeInterval(70)), .starting)
        XCTAssertEqual(t.update(healthOK: false, launchdRunning: true, now: t0.addingTimeInterval(80)), .starting)
        XCTAssertEqual(t.update(healthOK: false, launchdRunning: true, now: t0.addingTimeInterval(90)), .notResponding)
        XCTAssertEqual(t.update(healthOK: false, launchdRunning: false, now: t0.addingTimeInterval(100)), .stopped)
        XCTAssertEqual(t.update(healthOK: false, launchdRunning: nil, now: t0.addingTimeInterval(110)), .stopped)
        XCTAssertEqual(EngineState.running.text, "Sentry Six Manager is running")
        XCTAssertEqual(EngineState.starting.text, "Starting…")
        XCTAssertEqual(EngineState.notResponding.text, "Not responding")
        XCTAssertEqual(EngineStateTracker.pollInterval(onBattery: true), 15)
        XCTAssertEqual(EngineStateTracker.pollInterval(onBattery: false), 10)
    }

    func testMenuBarWords() {
        let rows: [(type: String, status: String)] = [("conversion", "running"), ("conversion", "done"), ("scan", "running"), ("conversion", "running")]
        XCTAssertEqual(MenuBarText.runningConversions(rows), 2)
        XCTAssertEqual(MenuBarText.conversions(2), "Converting: 2 jobs")
        XCTAssertEqual(MenuBarText.conversions(1), "Converting: 1 job")
        XCTAssertEqual(MenuBarText.conversions(0), "No conversions running")
        XCTAssertEqual(MenuBarText.conversions(nil), "Sign in to see conversions")
        XCTAssertNil(MenuBarText.stopConfirm(running: 0))
        XCTAssertEqual(MenuBarText.stopConfirm(running: 2), "Stop Sentry Six Manager? 2 conversions are running. They pause safely and continue when it starts again.")
        XCTAssertEqual(MenuBarText.stopConfirm(running: 1), "Stop Sentry Six Manager? 1 conversion is running. It pauses safely and continues when it starts again.")
    }

    func testAdminLines() {
        XCTAssertEqual(ServiceText.adminReason(.install), "Your administrator password is needed to install the background service.")
        XCTAssertEqual(ServiceText.adminReason(.start), "Your administrator password is needed to start the service.")
        XCTAssertEqual(ServiceText.adminReason(.setupCode), "Your administrator password is needed to show the setup code.")
    }

    func testEngineVersions() {
        XCTAssertEqual(EngineVersionCompare.compare(appVersion: "1.9.0", appSha: "a", engineVersion: "1.9.0", engineSha: "a"), .same)
        XCTAssertEqual(EngineVersionCompare.compare(appVersion: "1.10.0", appSha: "a", engineVersion: "1.9.0", engineSha: "b"), .appNewer)
        XCTAssertEqual(EngineVersionCompare.compare(appVersion: "1.9.0", appSha: "a", engineVersion: "1.10.0", engineSha: "b"), .serviceNewer)
        // same version, another build: the app's is offered
        XCTAssertEqual(EngineVersionCompare.compare(appVersion: "1.9.0", appSha: "a", engineVersion: "1.9.0", engineSha: "b"), .appNewer)
        XCTAssertEqual(EngineVersionCompare.compare(appVersion: "1.9.0", appSha: "a", engineVersion: nil, engineSha: nil), .appNewer)
    }
}

final class AgentPlistTests: XCTestCase {
    func testTheJobBothWays() throws {
        let bundled = AgentPlist.job()
        XCTAssertEqual(bundled["Label"] as? String, "io.github.skittles-fivem.sentry-six-manager.agent")
        XCTAssertEqual(bundled["BundleProgram"] as? String, "Contents/MacOS/sentry-six-engine")
        XCTAssertNil(bundled["Program"])
        XCTAssertEqual(bundled["Umask"] as? Int, 0o027)
        let fallback = try PropertyListSerialization.propertyList(from: AgentPlist.xml(program: "/Applications/Sentry Six Manager.app/Contents/MacOS/sentry-six-engine"), format: nil) as? [String: Any]
        XCTAssertEqual(fallback?["Program"] as? String, "/Applications/Sentry Six Manager.app/Contents/MacOS/sentry-six-engine")
        XCTAssertNil(fallback?["BundleProgram"])
        XCTAssertEqual((fallback?["KeepAlive"] as? [String: Bool])?["SuccessfulExit"], false)
    }

    /// AgentPlist.job() is the bundled plist (bundle/...agent.plist), key for key.
    func testTheBundledPlistIsTheSameJob() throws {
        let file = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("bundle/\(ServiceNames.agentPlistName)")
        let bundled = try XCTUnwrap(PropertyListSerialization.propertyList(from: Data(contentsOf: file), format: nil) as? NSDictionary)
        XCTAssertEqual(bundled, AgentPlist.job() as NSDictionary)
    }

    func testEngineJSON() throws {
        let json = #"{"v":1,"port":9000,"pid":412,"version":"1.9.0","sha":"abc","tls":{"mode":"own","caPem":null,"caSha256":null,"leafPem":"x","leafSha256":"AB"},"writtenAt":1,"later":true}"#
        let f = try XCTUnwrap(PublicEngineFile.parse(Data(json.utf8)))
        XCTAssertEqual(f.port, 9000)
        XCTAssertEqual(f.tls?.mode, "own")
        XCTAssertNil(f.tls?.caPem)
        XCTAssertNil(PublicEngineFile.parse(Data(#"{"v":2,"port":9000}"#.utf8)))
        XCTAssertNil(PublicEngineFile.parse(Data(#"{"v":1,"port":0}"#.utf8)))
        XCTAssertNotNil(PublicEngineFile.parse(Data(#"{"v":1,"port":9000,"tls":null}"#.utf8)))
        let pem = "-----BEGIN CERTIFICATE-----\nAAEC\nAw==\n-----END CERTIFICATE-----\n"
        XCTAssertEqual(PublicEngineFile.der(fromPEM: pem), Data([0, 1, 2, 3]))
        XCTAssertNil(PublicEngineFile.der(fromPEM: "-----BEGIN PRIVATE KEY-----\nAAEC\n-----END PRIVATE KEY-----"))
    }
}

final class FingerprintTests: XCTestCase {
    func testColonHex() {
        XCTAssertEqual(Fingerprint.colonHex([0x0a, 0xff, 0x00]), "0A:FF:00")
        XCTAssertEqual(Fingerprint.colonHex([]), "")
    }
}
