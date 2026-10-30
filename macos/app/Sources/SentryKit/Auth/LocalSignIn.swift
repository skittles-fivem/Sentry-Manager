import Foundation
#if canImport(Glibc)
import Glibc
#elseif canImport(Darwin)
import Darwin
#endif

/// Same-Mac sign-in, the app's side (2.6): find and check the socket, read
/// its one line, and trade the token for a session at POST /api/auth/local.
public enum LocalSignIn {
    /// sun_path holds 103 bytes and a NUL on macOS
    public static let maxSocketPath = 103
    public static let maxLine = 4096
    public static let readTimeout: TimeInterval = 5
    public static let tokenPrefix = "local1_"

    /// The socket's path for a mode; a too long login path falls back to
    /// /private/tmp/sentrysix-<uid>/signin.sock (the launcher makes it).
    public static func socketPath(mode: ServiceMode, home: String, uid: UInt32) -> String {
        switch mode {
        case .background:
            return "/Library/Application Support/SentrySixManager Local/signin.sock"
        case .login:
            let p = home + "/Library/Application Support/SentrySixManager/run/signin.sock"
            return p.utf8.count <= maxSocketPath ? p : "/private/tmp/sentrysix-\(uid)/signin.sock"
        }
    }

    public enum CheckResult: Sendable, Equatable {
        case ok
        case missing
        case refused(String)
    }

    /// The folder must be the right owner's, mode 0700, no symlinks; the
    /// socket must be a socket. Only then does the app connect.
    public static func check(socket: String, mode: ServiceMode, fs: FileChecking) -> CheckResult {
        let folder = (socket as NSString).deletingLastPathComponent
        let owner = mode == .background ? fs.uid(ofUser: EnginePaths.serviceAccount) : fs.currentUID
        guard let owner else { return .refused("no \(EnginePaths.serviceAccount) account") }
        guard let dir = fs.status(folder) else { return .missing }
        if dir.kind == .symlink { return .refused("the folder is a symlink") }
        if dir.kind != .directory { return .refused("the folder is not a folder") }
        if dir.ownerUID != owner { return .refused("the folder belongs to someone else") }
        if dir.mode & 0o777 != 0o700 { return .refused("the folder is not 0700") }
        guard let sock = fs.status(socket) else { return .missing }
        if sock.kind == .symlink { return .refused("the socket is a symlink") }
        if sock.kind != .socket { return .refused("not a socket") }
        return .ok
    }

    /// What the line on the socket said.
    public enum Line: Sendable, Equatable {
        case token(String, expiresAt: Double?)
        case setupCode(String)
        case off
        case busy
        case invalid(String)
    }

    struct Wire: Decodable {
        var v: Double?
        var token: String?
        var expiresAt: Double?
        var port: Double?
        var needsSetup: Bool?
        var setupCode: String?
        var error: String?
    }

    /// Parses the line (without or with its newline). `port` is engine.json's.
    public static func parse(_ data: Data, enginePort: Int) -> Line {
        if data.count > maxLine { return .invalid("line too long") }
        var bytes = data
        if bytes.last == 0x0a { bytes.removeLast() }
        if bytes.last == 0x0d { bytes.removeLast() }
        guard let w = try? JSONDecoder().decode(Wire.self, from: bytes) else { return .invalid("not JSON") }
        guard w.v == 1 else { return .invalid("unknown version") }
        switch w.error {
        case "off"?: return .off
        case "busy"?: return .busy
        case let e?: return .invalid("error \(e)")
        case nil: break
        }
        guard let port = w.port, let n = Int(exactly: port), n == enginePort else { return .invalid("wrong port") }
        if w.needsSetup == true, let code = w.setupCode, isSetupCode(code) { return .setupCode(code) }
        if let t = w.token, isToken(t) { return .token(t, expiresAt: w.expiresAt) }
        return .invalid("no token")
    }

    /// local1_ plus 43 base64url characters: 50 in all.
    public static func isToken(_ t: String) -> Bool {
        guard t.hasPrefix(tokenPrefix), t.utf8.count == 50 else { return false }
        return t.utf8.dropFirst(tokenPrefix.utf8.count).allSatisfy { c in
            (c >= 0x41 && c <= 0x5a) || (c >= 0x61 && c <= 0x7a) || (c >= 0x30 && c <= 0x39) || c == 0x2d || c == 0x5f
        }
    }

    public static func isSetupCode(_ s: String) -> Bool {
        let parts = s.split(separator: "-", omittingEmptySubsequences: false)
        return parts.count == 3 && parts.allSatisfy { $0.count == 5 && $0.allSatisfy { $0.isASCII && ($0.isUppercase || $0.isNumber) } }
    }
}

/// Reads one line from a Unix socket: at most 4 KiB, at most 5 s.
public enum UnixSocketLine {
    public enum Failure: Error, Equatable { case pathTooLong, connect(Int32), timedOut, tooLong, closedEarly }

    public static func read(path: String, timeout: TimeInterval = LocalSignIn.readTimeout, maxBytes: Int = LocalSignIn.maxLine) throws -> Data {
        #if canImport(Glibc)
        let fd = socket(AF_UNIX, Int32(SOCK_STREAM.rawValue), 0)
        #else
        let fd = socket(AF_UNIX, SOCK_STREAM, 0)
        #endif
        guard fd >= 0 else { throw Failure.connect(errno) }
        defer { close(fd) }
        var addr = sockaddr_un()
        addr.sun_family = sa_family_t(AF_UNIX)
        let bytes = Array(path.utf8)
        let capacity = MemoryLayout.size(ofValue: addr.sun_path)
        guard bytes.count < capacity else { throw Failure.pathTooLong }
        withUnsafeMutableBytes(of: &addr.sun_path) { buf in
            for (i, b) in bytes.enumerated() { buf[i] = b }
            buf[bytes.count] = 0
        }
        let len = socklen_t(MemoryLayout<sockaddr_un>.size)
        let rc = withUnsafePointer(to: &addr) { p in
            p.withMemoryRebound(to: sockaddr.self, capacity: 1) { connect(fd, $0, len) }
        }
        guard rc == 0 else { throw Failure.connect(errno) }
        let deadline = Date().addingTimeInterval(timeout)
        var out = Data()
        var chunk = [UInt8](repeating: 0, count: 1024)
        while true {
            let left = deadline.timeIntervalSinceNow
            if left <= 0 { throw Failure.timedOut }
            var pfd = pollfd(fd: fd, events: Int16(POLLIN), revents: 0)
            let ready = poll(&pfd, 1, Int32(max(1, left * 1000)))
            if ready == 0 { throw Failure.timedOut }
            if ready < 0 { if errno == EINTR { continue }; throw Failure.closedEarly }
            let n = recv(fd, &chunk, chunk.count, 0)
            if n < 0 { if errno == EINTR { continue }; throw Failure.closedEarly }
            if n == 0 { throw Failure.closedEarly }
            out.append(contentsOf: chunk[0..<n])
            if let nl = out.firstIndex(of: 0x0a) { return out.prefix(through: nl) }
            if out.count > maxBytes { throw Failure.tooLong }
        }
    }
}

/// The whole same-Mac sign-in, with what the app does after it (2.6, "In the app").
public actor LocalSignInFlow {
    public enum Outcome: Sendable, Equatable {
        case signedIn(user: String?)
        /// no account yet: open "Create your account" with the code filled in
        case needsSetup(code: String?)
        /// show the sign-in sheet; `message` is the server's text when it gave one
        case showSheet(message: String?)
    }

    private let client: APIClient
    private let socketPath: String
    private let mode: ServiceMode
    private let fs: FileChecking
    private let enginePort: Int
    private let readLine: @Sendable (String) throws -> Data
    private let sleep: @Sendable (Double) async -> Void
    /// false after Sign Out, until the person asks again or relaunches
    public private(set) var automatic = true

    public init(client: APIClient, socketPath: String, mode: ServiceMode, enginePort: Int, fs: FileChecking = PosixFileSystem(),
                readLine: @escaping @Sendable (String) throws -> Data = { try UnixSocketLine.read(path: $0) },
                sleep: @escaping @Sendable (Double) async -> Void = { try? await Task.sleep(nanoseconds: UInt64($0 * 1e9)) }) {
        self.client = client; self.socketPath = socketPath; self.mode = mode; self.enginePort = enginePort
        self.fs = fs; self.readLine = readLine; self.sleep = sleep
    }

    public func signedOut() { automatic = false }

    /// At launch or after a lost session: only while automatic.
    public func runAutomatically() async -> Outcome {
        guard automatic else { return .showSheet(message: nil) }
        return await run()
    }

    /// "Sign in on this Mac": always runs, and turns automatic back on.
    public func run() async -> Outcome {
        automatic = true
        guard LocalSignIn.check(socket: socketPath, mode: mode, fs: fs) == .ok else { return .showSheet(message: nil) }
        var line = readOnce()
        if line == .busy {
            await sleep(1)
            line = readOnce()
        }
        switch line {
        case .token(let token, _):
            return await exchange(token)
        case .setupCode(let code):
            return .needsSetup(code: code)
        case .off, .busy, .invalid:
            return .showSheet(message: nil)
        }
    }

    private func readOnce() -> LocalSignIn.Line {
        guard let data = try? readLine(socketPath) else { return .invalid("no line") }
        return LocalSignIn.parse(data, enginePort: enginePort)
    }

    private func exchange(_ token: String) async -> Outcome {
        do {
            let a = try await client.send(API.localSignIn(token: token))
            return .signedIn(user: a.user)
        } catch APIError.http(let status, let message, _) {
            if status == 409 && message == "Create the account first." { return .needsSetup(code: nil) }
            return .showSheet(message: message)
        } catch {
            return .showSheet(message: nil)
        }
    }
}
