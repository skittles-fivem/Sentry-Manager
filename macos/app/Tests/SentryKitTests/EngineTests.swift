import Foundation
import XCTest
@testable import SentryKit
#if canImport(Glibc)
import Glibc
#elseif canImport(Darwin)
import Darwin
#endif

final class EngineTests: XCTestCase {
    func pems() throws -> (ca: String, leaf: String, fps: [String: String]) {
        let ca = String(decoding: try fixture("certs/ca.pem"), as: UTF8.self)
        let leaf = String(decoding: try fixture("certs/leaf.pem"), as: UTF8.self)
        let fps = try JSON.decode([String: String].self, from: try fixture("certs/fingerprints.json"))
        return (ca, leaf, fps)
    }

    func engineJSON(_ ca: String, _ caFP: String, leaf: String, leafFP: String, v: Int = 1, mode: String = "local-ca") throws -> Data {
        let o: [String: JSONValue] = [
            "v": .number(Double(v)), "port": .number(9000), "pid": .number(412), "version": .string("1.9.0"), "sha": .string("f45795b"),
            "tls": .object(["mode": .string(mode), "caPem": mode == "own" ? .null : .string(ca), "caSha256": mode == "own" ? .null : .string(caFP),
                            "leafPem": .string(leaf), "leafSha256": .string(leafFP)]),
            "writtenAt": .number(1793000000000),
        ]
        return try JSON.encode(o)
    }

    func testSHA256MatchesOpenSSL() throws {
        let (ca, leaf, fps) = try pems()
        XCTAssertEqual(SHA256.fingerprint(PEM.certificates(ca)[0]), fps["caSha256"])
        XCTAssertEqual(SHA256.fingerprint(PEM.certificates(leaf)[0]), fps["leafSha256"])
        XCTAssertEqual(SHA256.hex(Data()), "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
        XCTAssertEqual(SHA256.hex(Data("abc".utf8)), "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")
        XCTAssertEqual(SHA256.hex(Data(repeating: 0x61, count: 1_000)), "41edece42d63e8d9bf515a9ba6932e1c20cbc9f5a5d134645adb5db1b9737ea3")
    }

    func paths(_ mode: ServiceMode) -> EnginePaths { EnginePaths(mode: mode, home: "/Users/ann") }

    func fs(_ mode: ServiceMode, file: Data, dirOwner: UInt32? = nil, fileOwner: UInt32? = nil, dirMode: UInt16 = 0o755, fileMode: UInt16 = 0o644,
            dirKind: FileStatus.Kind = .directory, fileKind: FileStatus.Kind = .file) -> FakeFS {
        let p = paths(mode)
        let owner: UInt32 = mode == .background ? 290 : 501
        var f = FakeFS()
        f.entries[p.publicFolder] = FileStatus(kind: dirKind, ownerUID: dirOwner ?? owner, mode: dirMode)
        f.entries[p.engineJSON] = FileStatus(kind: fileKind, ownerUID: fileOwner ?? owner, mode: fileMode)
        f.files[p.engineJSON] = file
        return f
    }

    func testEngineJSONChecks() throws {
        let (ca, leaf, fps) = try pems()
        let good = try engineJSON(ca, fps["caSha256"]!, leaf: leaf, leafFP: fps["leafSha256"]!)
        for mode in ServiceMode.allCases {
            guard case .found(let info) = EngineLocator(paths: paths(mode), fs: fs(mode, file: good)).lookup() else { return XCTFail("\(mode)") }
            XCTAssertEqual(info.baseURL?.absoluteString, "https://127.0.0.1:9000")
            if case .refused = EngineLocator(paths: paths(mode), fs: fs(mode, file: good, dirKind: .symlink)).lookup() {} else { XCTFail("symlink folder") }
            if case .refused = EngineLocator(paths: paths(mode), fs: fs(mode, file: good, fileKind: .symlink)).lookup() {} else { XCTFail("symlink file") }
            if case .refused = EngineLocator(paths: paths(mode), fs: fs(mode, file: good, dirMode: 0o775)).lookup() {} else { XCTFail("group-writable folder") }
            if case .refused = EngineLocator(paths: paths(mode), fs: fs(mode, file: good, fileMode: 0o666)).lookup() {} else { XCTFail("world-writable file") }
            if case .refused = EngineLocator(paths: paths(mode), fs: fs(mode, file: good, fileOwner: 777)).lookup() {} else { XCTFail("someone else's file") }
            if case .refused = EngineLocator(paths: paths(mode), fs: fs(mode, file: good, dirOwner: 777)).lookup() {} else { XCTFail("someone else's folder") }
        }
        // the user's own file is not the service's
        if case .refused = EngineLocator(paths: paths(.background), fs: fs(.background, file: good, dirOwner: 501, fileOwner: 501)).lookup() {} else { XCTFail() }
        XCTAssertEqual(EngineLocator(paths: paths(.login), fs: FakeFS()).lookup(), .missing)
        XCTAssertEqual(paths(.background).engineJSON, "/Library/Application Support/SentrySixManager Public/engine.json")
        XCTAssertEqual(paths(.login).engineJSON, "/Users/ann/Library/Application Support/SentrySixManager/public/engine.json")
    }

    func testEngineJSONParsing() throws {
        let (ca, leaf, fps) = try pems()
        XCTAssertEqual(EngineLocator.parse(try engineJSON(ca, fps["caSha256"]!, leaf: leaf, leafFP: fps["leafSha256"]!, v: 2)), .tooNew)
        if case .refused = EngineLocator.parse(json("not json")) {} else { XCTFail() }
        if case .refused = EngineLocator.parse(json(#"{"v":1,"port":0,"tls":{"mode":"own","leafSha256":"AA"}}"#)) {} else { XCTFail("no port") }
        if case .found = EngineLocator.parse(json(#"{"v":1,"port":9000,"tls":{"mode":"own","caPem":null,"caSha256":null,"leafSha256":"AA"},"newKey":1}"#)) {} else { XCTFail("own mode") }
    }

    func testPinPolicyLocalCA() throws {
        let (ca, leaf, fps) = try pems()
        let tls = EngineTLS(mode: "local-ca", caPem: ca, caSha256: fps["caSha256"], leafPem: leaf, leafSha256: fps["leafSha256"])
        let pin = try XCTUnwrap(PinPolicy(tls))
        let chain = [PEM.certificates(leaf)[0], PEM.certificates(ca)[0]]
        XCTAssertEqual(pin.evaluate(.init(chain: chain, anchoredEvaluationPassed: true, leafValidNow: true, leafNamesLoopback: true)), .accept)
        // a renewed leaf under the same CA still passes: the leaf's own print is not pinned
        XCTAssertEqual(pin.evaluate(.init(chain: [Data([1, 2, 3]), PEM.certificates(ca)[0]], anchoredEvaluationPassed: true, leafValidNow: true, leafNamesLoopback: true)), .accept)
        guard case .reject(let why, let expected, _) = pin.evaluate(.init(chain: chain, anchoredEvaluationPassed: false, leafValidNow: true, leafNamesLoopback: true)) else { return XCTFail() }
        XCTAssertEqual(why, "not signed by the pinned CA")
        XCTAssertEqual(expected, fps["caSha256"])
        if case .accept = pin.evaluate(.init(chain: chain, anchoredEvaluationPassed: true, leafValidNow: false, leafNamesLoopback: true)) { XCTFail("expired leaf") }
        if case .accept = pin.evaluate(.init(chain: chain, anchoredEvaluationPassed: true, leafValidNow: true, leafNamesLoopback: false)) { XCTFail("wrong name") }
        if case .accept = pin.evaluate(.init(chain: [], anchoredEvaluationPassed: true, leafValidNow: true, leafNamesLoopback: true)) { XCTFail("no chain") }
        // engine.json's print must match its own CA
        XCTAssertNil(PinPolicy(EngineTLS(mode: "local-ca", caPem: ca, caSha256: fps["leafSha256"], leafPem: nil, leafSha256: nil)))
    }

    func testPinPolicyOwnAndReread() throws {
        let (_, leaf, fps) = try pems()
        let pin = try XCTUnwrap(PinPolicy(EngineTLS(mode: "own", caPem: nil, caSha256: nil, leafPem: leaf, leafSha256: fps["leafSha256"]!.lowercased())))
        let der = PEM.certificates(leaf)[0]
        // own mode compares the leaf, whatever the system says
        XCTAssertEqual(pin.evaluate(.init(chain: [der], anchoredEvaluationPassed: false, leafValidNow: false, leafNamesLoopback: false)), .accept)
        guard case .reject(_, let e, let p) = pin.evaluate(.init(chain: [Data([9])], anchoredEvaluationPassed: true, leafValidNow: true, leafNamesLoopback: true)) else { return XCTFail() }
        XCTAssertEqual(e, fps["leafSha256"])
        XCTAssertEqual(p, SHA256.fingerprint(Data([9])))
        // a CA replaced: re-read once, then the pinning screen
        var retry = PinRetry()
        XCTAssertEqual(retry.rejected(), .rereadAndRetry)
        XCTAssertEqual(retry.rejected(), .showPinningScreen)
        retry.accepted()
        XCTAssertEqual(retry.rejected(), .rereadAndRetry)
        XCTAssertEqual(Strings.Pinning.expected("AA"), "Expected: AA")
    }
}

/// A Unix socket server in a temp folder that writes one canned line.
final class LineServer: @unchecked Sendable {
    let dir: String
    let path: String
    private var fd: Int32 = -1
    private var thread: Thread?

    init(line: Data?, delay: Double = 0) throws {
        dir = NSTemporaryDirectory() + "sk-\(UUID().uuidString.prefix(8))"
        try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        path = dir + "/signin.sock"
        #if canImport(Glibc)
        fd = socket(AF_UNIX, Int32(SOCK_STREAM.rawValue), 0)
        #else
        fd = socket(AF_UNIX, SOCK_STREAM, 0)
        #endif
        var addr = sockaddr_un()
        addr.sun_family = sa_family_t(AF_UNIX)
        let bytes = Array(path.utf8)
        withUnsafeMutableBytes(of: &addr.sun_path) { b in for (i, c) in bytes.enumerated() { b[i] = c }; b[bytes.count] = 0 }
        let rc = withUnsafePointer(to: &addr) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { bind(fd, $0, socklen_t(MemoryLayout<sockaddr_un>.size)) } }
        guard rc == 0, listen(fd, 4) == 0 else { throw NSError(domain: "bind", code: Int(errno)) }
        let server = fd
        thread = Thread {
            let c = accept(server, nil, nil)
            guard c >= 0 else { return }
            if delay > 0 { usleep(useconds_t(delay * 1_000_000)) }
            if let line {
                _ = line.withUnsafeBytes { send(c, $0.baseAddress, line.count, 0) }
            }
            if delay >= 0 { usleep(200_000) }
            close(c)
        }
        thread!.start()
    }

    func stop() {
        close(fd)
        try? FileManager.default.removeItem(atPath: dir)
    }
}

final class LocalSignInTests: XCTestCase {
    func testSocketPaths() {
        XCTAssertEqual(LocalSignIn.socketPath(mode: .background, home: "/Users/ann", uid: 501), "/Library/Application Support/SentrySixManager Local/signin.sock")
        XCTAssertEqual(LocalSignIn.socketPath(mode: .login, home: "/Users/ann", uid: 501), "/Users/ann/Library/Application Support/SentrySixManager/run/signin.sock")
        let long = "/Users/" + String(repeating: "a", count: 60)
        XCTAssertEqual(LocalSignIn.socketPath(mode: .login, home: long, uid: 501), "/private/tmp/sentrysix-501/signin.sock")
        // exactly 103 bytes still fits
        let base = "/Library/Application Support/SentrySixManager/run/signin.sock"
        let fits = "/" + String(repeating: "b", count: 103 - base.utf8.count - 1)
        XCTAssertEqual(LocalSignIn.socketPath(mode: .login, home: fits, uid: 1).utf8.count, 103)
    }

    func testFolderChecks() {
        let sock = "/x/run/signin.sock"
        func fs(_ dir: FileStatus?, _ s: FileStatus?) -> FakeFS {
            var f = FakeFS()
            f.entries["/x/run"] = dir
            f.entries[sock] = s
            return f
        }
        let okDir = FileStatus(kind: .directory, ownerUID: 501, mode: 0o700)
        let okSock = FileStatus(kind: .socket, ownerUID: 501, mode: 0o666)
        XCTAssertEqual(LocalSignIn.check(socket: sock, mode: .login, fs: fs(okDir, okSock)), .ok)
        XCTAssertEqual(LocalSignIn.check(socket: sock, mode: .login, fs: fs(nil, nil)), .missing)
        XCTAssertNotEqual(LocalSignIn.check(socket: sock, mode: .login, fs: fs(FileStatus(kind: .directory, ownerUID: 501, mode: 0o755), okSock)), .ok)
        XCTAssertNotEqual(LocalSignIn.check(socket: sock, mode: .login, fs: fs(FileStatus(kind: .symlink, ownerUID: 501, mode: 0o700), okSock)), .ok)
        XCTAssertNotEqual(LocalSignIn.check(socket: sock, mode: .login, fs: fs(okDir, FileStatus(kind: .file, ownerUID: 501, mode: 0o600))), .ok)
        XCTAssertNotEqual(LocalSignIn.check(socket: sock, mode: .login, fs: fs(okDir, FileStatus(kind: .symlink, ownerUID: 501, mode: 0o777))), .ok)
        // the service's folder belongs to _sentrysix, not the user
        XCTAssertNotEqual(LocalSignIn.check(socket: sock, mode: .background, fs: fs(okDir, okSock)), .ok)
        XCTAssertEqual(LocalSignIn.check(socket: sock, mode: .background, fs: fs(FileStatus(kind: .directory, ownerUID: 290, mode: 0o700), okSock)), .ok)
    }

    static let token = "local1_AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8"

    func testLineShapes() {
        func p(_ s: String) -> LocalSignIn.Line { LocalSignIn.parse(Data(s.utf8), enginePort: 9000) }
        XCTAssertEqual(p(#"{"v":1,"token":"\#(Self.token)","expiresAt":1793000060000,"port":9000}"# + "\n"), .token(Self.token, expiresAt: 1793000060000))
        XCTAssertEqual(p(#"{"v":1,"needsSetup":true,"setupCode":"ABCDE-FGHIJ-KLMNO","port":9000}"#), .setupCode("ABCDE-FGHIJ-KLMNO"))
        XCTAssertEqual(p(#"{"v":1,"error":"off"}"#), .off)
        XCTAssertEqual(p(#"{"v":1,"error":"busy"}"#), .busy)
        XCTAssertEqual(p(#"{"v":1,"token":"\#(Self.token)","port":9001}"#), .invalid("wrong port"))
        XCTAssertEqual(p(#"{"v":1,"token":"\#(Self.token)","port":9000,"future":[1,2]}"#), .token(Self.token, expiresAt: nil))
        XCTAssertEqual(p(#"{"v":2,"token":"\#(Self.token)","port":9000}"#), .invalid("unknown version"))
        XCTAssertEqual(p(#"{"v":1,"token":"local1_short","port":9000}"#), .invalid("no token"))
        XCTAssertEqual(p(#"{"v":1,"token":"local1_AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdH+8","port":9000}"#), .invalid("no token"))
        XCTAssertEqual(LocalSignIn.parse(Data(repeating: 0x20, count: 4097), enginePort: 9000), .invalid("line too long"))
    }

    func testRealUnixSocket() throws {
        let line = Data(#"{"v":1,"token":"\#(Self.token)","expiresAt":1,"port":9000}"#.utf8) + Data([0x0a])
        let s = try LineServer(line: line)
        defer { s.stop() }
        let got = try UnixSocketLine.read(path: s.path, timeout: 5)
        XCTAssertEqual(LocalSignIn.parse(got, enginePort: 9000), .token(Self.token, expiresAt: 1))
    }

    func testUnixSocketNoNewlineTimesOut() throws {
        let s = try LineServer(line: Data(#"{"v":1"#.utf8), delay: 0)
        defer { s.stop() }
        XCTAssertThrowsError(try UnixSocketLine.read(path: s.path, timeout: 0.1)) { e in
            XCTAssertEqual(e as? UnixSocketLine.Failure, .timedOut)
        }
    }

    func testUnixSocketTooLongAndClosed() throws {
        let s = try LineServer(line: Data(repeating: 0x41, count: 5000))
        defer { s.stop() }
        XCTAssertThrowsError(try UnixSocketLine.read(path: s.path, timeout: 2)) { e in XCTAssertEqual(e as? UnixSocketLine.Failure, .tooLong) }
        let s2 = try LineServer(line: nil)
        defer { s2.stop() }
        XCTAssertThrowsError(try UnixSocketLine.read(path: s2.path, timeout: 2)) { e in XCTAssertEqual(e as? UnixSocketLine.Failure, .closedEarly) }
        XCTAssertThrowsError(try UnixSocketLine.read(path: "/nonexistent/x.sock", timeout: 1))
    }

    func okFS(_ path: String) -> FakeFS {
        var f = FakeFS()
        f.entries[(path as NSString).deletingLastPathComponent] = FileStatus(kind: .directory, ownerUID: 501, mode: 0o700)
        f.entries[path] = FileStatus(kind: .socket, ownerUID: 501, mode: 0o666)
        return f
    }

    func testExchangeRequest() async throws {
        let t = FakeTransport { _ in .init(status: 200, body: json(#"{"ok":true,"user":"admin"}"#), headers: ["Set-Cookie": "__Host-sm_session=S1; HttpOnly; Secure"]) }
        let c = makeClient(t, token: nil)
        let line = Data(#"{"v":1,"token":"\#(Self.token)","port":9000}"#.utf8) + Data([0x0a])
        let flow = LocalSignInFlow(client: c, socketPath: "/r/signin.sock", mode: .login, enginePort: 9000, fs: okFS("/r/signin.sock"), readLine: { _ in line })
        let out = await flow.run()
        XCTAssertEqual(out, .signedIn(user: "admin"))
        let r = try XCTUnwrap(t.requests.first)
        XCTAssertEqual(r.url?.path, "/api/auth/local")
        XCTAssertEqual(r.httpMethod, "POST")
        XCTAssertEqual(r.value(forHTTPHeaderField: "X-Requested-With"), "sentry-six-manager")
        XCTAssertEqual(r.value(forHTTPHeaderField: "Content-Type"), "application/json")
        XCTAssertNil(r.value(forHTTPHeaderField: "Origin"))
        XCTAssertNil(r.value(forHTTPHeaderField: "Cookie"))
        XCTAssertEqual(r.bodyString, #"{"token":"\#(Self.token)"}"#)
        XCTAssertFalse(r.url!.absoluteString.contains("local1_"), "never in a URL")
        XCTAssertEqual(c.session.token(), "S1")
    }

    func testFallbacks() async throws {
        let refused = FakeTransport { _ in .init(status: 403, body: json(#"{"error":"Same-computer sign-in is turned off. Sign in with your password."}"#)) }
        let tokenLine = Data(#"{"v":1,"token":"\#(Self.token)","port":9000}"#.utf8)
        var flow = LocalSignInFlow(client: makeClient(refused, token: nil), socketPath: "/r/s.sock", mode: .login, enginePort: 9000, fs: okFS("/r/s.sock"), readLine: { _ in tokenLine })
        var out = await flow.run()
        XCTAssertEqual(out, .showSheet(message: "Same-computer sign-in is turned off. Sign in with your password."))

        let setup = FakeTransport { _ in .init(status: 409, body: json(#"{"error":"Create the account first.","needsSetup":true}"#)) }
        flow = LocalSignInFlow(client: makeClient(setup, token: nil), socketPath: "/r/s.sock", mode: .login, enginePort: 9000, fs: okFS("/r/s.sock"), readLine: { _ in tokenLine })
        out = await flow.run()
        XCTAssertEqual(out, .needsSetup(code: nil))

        // the setup code from the socket
        flow = LocalSignInFlow(client: makeClient(setup, token: nil), socketPath: "/r/s.sock", mode: .login, enginePort: 9000, fs: okFS("/r/s.sock"),
                               readLine: { _ in Data(#"{"v":1,"needsSetup":true,"setupCode":"ABCDE-FGHIJ-KLMNO","port":9000}"#.utf8) })
        out = await flow.run()
        XCTAssertEqual(out, .needsSetup(code: "ABCDE-FGHIJ-KLMNO"))

        // busy: one retry a second later, then the sheet
        let reads = Locked(0)
        let slept = Locked<[Double]>([])
        flow = LocalSignInFlow(client: makeClient(refused, token: nil), socketPath: "/r/s.sock", mode: .login, enginePort: 9000, fs: okFS("/r/s.sock"),
                               readLine: { _ in reads.update { $0 += 1 }; return Data(#"{"v":1,"error":"busy"}"#.utf8) },
                               sleep: { s in slept.update { $0.append(s) } })
        out = await flow.run()
        XCTAssertEqual(out, .showSheet(message: nil))
        XCTAssertEqual(reads.get(), 2)
        XCTAssertEqual(slept.get(), [1])

        // a folder that fails the check: no connect at all
        let connected = Locked(false)
        flow = LocalSignInFlow(client: makeClient(refused, token: nil), socketPath: "/r/s.sock", mode: .login, enginePort: 9000, fs: FakeFS(),
                               readLine: { _ in connected.set(true); return tokenLine })
        out = await flow.run()
        XCTAssertEqual(out, .showSheet(message: nil))
        XCTAssertFalse(connected.get())
    }

    func testNoAutomaticSignInAfterSignOut() async {
        let t = FakeTransport { _ in .init(status: 200, body: json(#"{"ok":true,"user":"admin"}"#)) }
        let reads = Locked(0)
        let flow = LocalSignInFlow(client: makeClient(t, token: nil), socketPath: "/r/s.sock", mode: .login, enginePort: 9000, fs: okFS("/r/s.sock"),
                                   readLine: { _ in reads.update { $0 += 1 }; return Data(#"{"v":1,"token":"\#(Self.token)","port":9000}"#.utf8) })
        await flow.signedOut()
        let auto = await flow.runAutomatically()
        XCTAssertEqual(auto, .showSheet(message: nil))
        XCTAssertEqual(reads.get(), 0)
        // "Sign in on this Mac" runs it, and turns automatic back on
        let manual = await flow.run()
        XCTAssertEqual(manual, .signedIn(user: "admin"))
        let again = await flow.runAutomatically()
        XCTAssertEqual(again, .signedIn(user: "admin"))
    }

    func testTokenNeverStoredInPrefs() async {
        // the flow has no prefs at all; the session store only gets the cookie
        let prefs = MemoryPrefs()
        let t = FakeTransport { _ in .init(status: 200, body: json(#"{"ok":true,"user":"admin"}"#), headers: ["Set-Cookie": "__Host-sm_session=S; Secure"]) }
        let c = makeClient(t, token: nil)
        let flow = LocalSignInFlow(client: c, socketPath: "/r/s.sock", mode: .login, enginePort: 9000, fs: okFS("/r/s.sock"),
                                   readLine: { _ in Data(#"{"v":1,"token":"\#(Self.token)","port":9000}"#.utf8) })
        _ = await flow.run()
        XCTAssertTrue(prefs.all.isEmpty)
        XCTAssertNotEqual(c.session.token(), Self.token)
    }
}
