import Foundation

/// Quoting for /bin/sh: one single-quoted word, `'` written as `'\''`.
public enum ShellQuote {
    public static func word(_ s: String) -> String {
        "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'"
    }

    /// No NUL, no newline: a path or name like that is refused before any prompt.
    public static func isSafe(_ s: String) -> Bool {
        !s.isEmpty && !s.contains("\u{0}") && !s.contains("\n") && !s.contains("\r")
    }
}

/// A Mac short user name as install.sh accepts it.
public enum MacUserName {
    public static func isValid(_ s: String) -> Bool {
        guard let first = s.unicodeScalars.first, s.utf8.count <= 255 else { return false }
        func ok(_ c: Unicode.Scalar, first: Bool) -> Bool {
            if c.isASCII && (CharacterSet.alphanumerics.contains(c) || c == "_") { return true }
            return !first && (c == "." || c == "-")
        }
        return ok(first, first: true) && s.unicodeScalars.dropFirst().allSatisfy { ok($0, first: false) }
    }
}

/// Errors shown as they are (plain words).
public struct ServiceError: Error, Equatable, Sendable, CustomStringConvertible {
    public let message: String
    public init(_ message: String) { self.message = message }
    public var description: String { message }
}

/// The text of each administrator step (3.4). It is built inside the app,
/// never read from the bundle: the bundle is user-writable and could change
/// between the click and the copy.
public enum InstallScript {
    public static let changedMessage = "The app's files were changed after it was opened. Download it again."
    public static let noEngineMessage = "This copy of the app has no engine to install. Download the app from the release page."

    /// Install (or with `update`, update) the background service from the app's engine.
    ///
    /// 1. A root-owned staging folder; 2. ditto the engine into it; 3. the
    /// manifest: its own SHA-256 must be the one compiled into the app, the
    /// files must be exactly the listed ones and match; 4. install.sh
    /// --from-dir on the staged copy only.
    public static func installDaemon(engine: String, manifestSHA256: String?, localUser: String, importData: String? = nil, update: Bool = false) throws -> String {
        guard let sha = manifestSHA256, sha.count == 64, sha.allSatisfy({ $0.isHexDigit && !$0.isUppercase }) else {
            throw ServiceError(noEngineMessage)
        }
        guard ShellQuote.isSafe(engine) else { throw ServiceError("The app is in a folder whose name cannot be used. Move it to Applications.") }
        guard MacUserName.isValid(localUser) else { throw ServiceError("\(localUser) is not a Mac user name.") }
        if let d = importData, !ShellQuote.isSafe(d) { throw ServiceError("The data folder's name cannot be used.") }
        let changed = ShellQuote.word(changedMessage)
        var args = ["--from-dir", "\"$S\"", "--service-mode", "daemon", "--local-user", ShellQuote.word(localUser)]
        if let d = importData { args += ["--import-data", ShellQuote.word(d)] }
        if update { args.append("--update") }
        return """
        set -eu
        umask 077
        S=$(/usr/bin/mktemp -d /Library/SentrySixManager.stage.XXXXXX)
        trap '/bin/rm -rf "$S"' EXIT
        /usr/bin/ditto \(ShellQuote.word(engine)) "$S"
        cd "$S"
        changed() { echo \(changed) >&2; exit 65; }
        [ -z "$(/usr/bin/find . -type l -print)" ] || changed
        [ "$(/usr/bin/shasum -a 256 MANIFEST.sha256 | /usr/bin/awk '{ print $1 }')" = '\(sha)' ] || changed
        [ "$(/usr/bin/find . -type f ! -path ./MANIFEST.sha256 | LC_ALL=C /usr/bin/sort)" = "$(/usr/bin/sed -E 's/^[0-9a-f]{64}  //' MANIFEST.sha256 | LC_ALL=C /usr/bin/sort)" ] || changed
        /usr/bin/shasum -a 256 -c MANIFEST.sha256 >/dev/null || changed
        umask 022
        /bin/bash "$S/install.sh" \(args.joined(separator: " "))
        """
    }

    /// One root-owned command of the installed service (Start, Stop, Setup code, ...).
    public static func cli(_ args: [String]) throws -> String {
        guard args.allSatisfy(ShellQuote.isSafe) else { throw ServiceError("That name cannot be used.") }
        return ([DaemonPaths.cli] + args).map(ShellQuote.word).joined(separator: " ")
    }

    public static func start() -> String { (try? cli(["start"])) ?? "" }
    public static func stop() -> String { (try? cli(["stop"])) ?? "" }
    public static func setupCode() -> String { (try? cli(["setup-code"])) ?? "" }

    public static func allowFolder(_ path: String, write: Bool = false) throws -> String {
        try cli(["allow-folder", path] + (write ? ["--write"] : []))
    }

    public static func localSignIn(allow: Bool, user: String) throws -> String {
        guard MacUserName.isValid(user) else { throw ServiceError("\(user) is not a Mac user name.") }
        return try cli(["local-signin", allow ? "allow" : "remove", user])
    }

    public static func uninstallDaemon(removeData: Bool) -> String {
        (["/bin/bash", DaemonPaths.uninstall] + (removeData ? ["--remove-data"] : [])).map(ShellQuote.word).joined(separator: " ")
    }

    /// Background service -> in your login (3.6): stop and switch off the
    /// service (its install stays), and copy its data to the user once, when
    /// the user has none. The copy into the home folder is made as the user,
    /// so root never writes through a link someone left there.
    public static func handBackToUser(user: String, home: String) throws -> String {
        guard MacUserName.isValid(user) else { throw ServiceError("\(user) is not a Mac user name.") }
        guard ShellQuote.isSafe(home), home.hasPrefix("/") else { throw ServiceError("Your home folder's name cannot be used.") }
        let paths = AgentPaths(home: home)
        let u = ShellQuote.word(user)
        let label = ShellQuote.word("system/\(ServiceNames.daemonLabel)")
        return """
        set -eu
        \(ShellQuote.word(DaemonPaths.cli)) stop
        /bin/launchctl bootout \(label) 2>/dev/null || true
        /bin/launchctl disable \(label)
        D=\(ShellQuote.word(paths.data))
        if [ ! -e "$D" ] && [ -d \(ShellQuote.word(DaemonPaths.data)) ]; then
          T=$(/usr/bin/mktemp -d /private/tmp/sentrysix-copy.XXXXXX)
          trap '/bin/rm -rf "$T"' EXIT
          /usr/bin/ditto \(ShellQuote.word(DaemonPaths.data)) "$T/data"
          /usr/sbin/chown -R \(u) "$T"
          /usr/bin/sudo -u \(u) /bin/mkdir -p \(ShellQuote.word(paths.state))
          /usr/bin/sudo -u \(u) /usr/bin/ditto "$T/data" "$D"
          /usr/bin/sudo -u \(u) /bin/chmod 0700 "$D"
          echo "Copied the service's data to your login."
        else
          echo "Kept the data already in your login."
        fi
        """
    }

    /// Wraps a step for `do shell script ... with administrator privileges`:
    /// bash runs it, and it is an AppleScript string literal. `prompt` is the
    /// line macOS shows in its password window. stderr stays apart: when a
    /// step fails, do shell script makes stderr the error's text, so the
    /// step's own words ("The app's files were changed...") reach the user.
    public static func appleScript(_ script: String, prompt: String) -> String {
        "do shell script \(appleScriptLiteral("/bin/bash -c " + ShellQuote.word(script))) with prompt \(appleScriptLiteral(prompt)) with administrator privileges without altering line endings"
    }

    /// What osascript's answer means: the output, Cancel, or the error's own words.
    public enum AdminOutcome: Equatable, Sendable {
        case ok(String)
        case cancelled
        case failed(String)
    }

    /// osascript prints "0:123: execution error: <message> (<number>)" on stderr;
    /// -128 is the user's Cancel.
    public static func adminOutcome(status: Int32, stdout: String, stderr: String) -> AdminOutcome {
        if status == 0 { return .ok(stdout) }
        let err = stderr.trimmingCharacters(in: .whitespacesAndNewlines)
        if err.hasSuffix("(-128)") { return .cancelled }
        var message = err
        if let r = message.range(of: "execution error: ") { message = String(message[r.upperBound...]) }
        if let r = message.range(of: #" \(-?\d+\)$"#, options: .regularExpression) { message = String(message[..<r.lowerBound]) }
        message = message.trimmingCharacters(in: .whitespacesAndNewlines)
        return .failed(message.isEmpty ? "The step did not finish." : message)
    }

    public static func appleScriptLiteral(_ s: String) -> String {
        "\"" + s.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"") + "\""
    }

    /// The `SETUP-CODE: XXXXX-XXXXX-XXXXX` line install.sh prints, or the
    /// CLI's "Setup code: ..." line. Kept in memory only.
    public static func setupCode(fromOutput text: String) -> String? {
        for raw in text.split(whereSeparator: { $0 == "\n" || $0 == "\r" }) {
            let line = raw.trimmingCharacters(in: .whitespaces)
            for prefix in ["SETUP-CODE: ", "Setup code: "] where line.hasPrefix(prefix) {
                let code = String(line.dropFirst(prefix.count))
                if LocalSignIn.isSetupCode(code) { return code }
            }
        }
        return nil
    }
}
