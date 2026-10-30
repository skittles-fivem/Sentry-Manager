import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
import XCTest
@testable import SentryKit

final class APIClientTests: XCTestCase {
    func testNothingGoesOutExceptOverHTTPSToLoopback() async throws {
        for base in ["http://127.0.0.1:9000", "https://example.com:9000"] {
            let t = FakeTransport { _ in .init(status: 200, body: json("{}")) }
            let c = APIClient(baseURL: URL(string: base)!, transport: t, session: MemorySessionStore(token: "TOKEN"))
            do {
                _ = try await c.send(API.login(username: "admin", password: "secret-password"))
                XCTFail("sent to \(base)")
            } catch let e as APIError {
                XCTAssertEqual(e, .unreachable)
            }
            XCTAssertTrue(t.requests.isEmpty, base)
            // streams and media build their requests here too: no cookie
            XCTAssertNil(c.makeRequest(method: .get, pathAndQuery: "/api/events").value(forHTTPHeaderField: "Cookie"))
        }
    }

    func testHeadersOnGetAndPost() async throws {
        let t = fixtureServer(["/api/vehicles": "vehicles.json", "/api/vehicles/model-y/scan": "protect.json"])
        let c = makeClient(t)
        _ = try await c.send(API.vehicles())
        _ = try await c.send(API.scan("model-y"))
        let get = t.requests[0], post = t.requests[1]
        XCTAssertNil(get.value(forHTTPHeaderField: "X-Requested-With"))
        XCTAssertEqual(post.value(forHTTPHeaderField: "X-Requested-With"), "sentry-six-manager")
        XCTAssertEqual(post.value(forHTTPHeaderField: "Content-Type"), "application/json")
        for r in t.requests {
            XCTAssertNil(r.value(forHTTPHeaderField: "Origin"))
            XCTAssertEqual(r.value(forHTTPHeaderField: "Cookie"), "__Host-sm_session=TOKEN")
            XCTAssertFalse(r.httpShouldHandleCookies)
        }
        XCTAssertEqual(post.httpMethod, "POST")
        XCTAssertEqual(post.bodyString, "{}")
    }

    func testPathParametersArePercentEncoded() {
        XCTAssertEqual(API.recording("a/b c", "x?y#z").path, "/api/vehicles/a%2Fb%20c/recordings/x%3Fy%23z")
        XCTAssertEqual(API.days("v", category: "Sentry", offset: 30, limit: 30, date: "2025-06-01").pathAndQuery,
                       "/api/vehicles/v/days?category=Sentry&date=2025-06-01&offset=30&limit=30")
        XCTAssertEqual(API.telemetry(path: "/Volumes/Car 1/TeslaCam/a&b.mp4").pathAndQuery,
                       "/api/telemetry?path=%2FVolumes%2FCar%201%2FTeslaCam%2Fa%26b.mp4")
        XCTAssertEqual(API.days("v", category: "all").pathAndQuery, "/api/vehicles/v/days?offset=0&limit=30")
    }

    func testErrorEnvelopeIsShownAsItIs() async throws {
        let t = FakeTransport { _ in .init(status: 400, body: json(#"{"error":"Saved: keep between 1 and 3650 days, or leave empty to keep forever."}"#)) }
        do {
            _ = try await makeClient(t).send(API.setPolicy("v", PolicyBody()))
            XCTFail("no error")
        } catch let e as APIError {
            XCTAssertEqual(e.message, "Saved: keep between 1 and 3650 days, or leave empty to keep forever.")
            XCTAssertEqual(e.status, 400)
        }
        // no body: the status text
        let t2 = FakeTransport { _ in .init(status: 502, body: Data()) }
        do { _ = try await makeClient(t2).send(API.vehicles()); XCTFail() } catch let e as APIError { XCTAssertEqual(e.message, "Bad Gateway") }
        // nothing answered
        let t3 = FakeTransport { _ in .init(status: 0, body: Data()) }
        do { _ = try await makeClient(t3).send(API.vehicles()); XCTFail() } catch let e as APIError {
            XCTAssertEqual(e, .unreachable)
            XCTAssertEqual(e.message, "Can't reach Sentry Six Manager.\nStart it from the menu bar.")
        }
    }

    func test401OutsideAuthSignsOut() async throws {
        let t = FakeTransport { req in
            req.url!.path == "/api/auth/login" ? .init(status: 401, body: try! apiFixture("login-401-wrong.json").bodyOnly())
                : .init(status: 401, body: json(#"{"error":"Sign in first."}"#))
        }
        let c = makeClient(t)
        let lost = Locked(0)
        await c.setOnSessionLost { lost.update { $0 += 1 } }
        do { _ = try await c.send(API.vehicles()); XCTFail() } catch let e as APIError { XCTAssertEqual(e, .signedOut) }
        XCTAssertNil(c.session.token())
        XCTAssertEqual(lost.get(), 1)
        // a wrong password is not a lost session
        c.session.setToken("T2")
        do { _ = try await c.send(API.login(username: "admin", password: "x")); XCTFail() } catch let e as APIError {
            XCTAssertEqual(e.message, "Wrong user name or password.")
        }
        XCTAssertEqual(c.session.token(), "T2")
        XCTAssertEqual(lost.get(), 1)
    }

    func testSetCookieStoresAndClearsTheSession() async throws {
        let set = String(decoding: try fixture("api/setup-set-cookie.txt"), as: UTF8.self).trimmingCharacters(in: .newlines)
        XCTAssertEqual(SessionCookie.change(fromSetCookie: set), .set("TOKEN"))
        XCTAssertEqual(SessionCookie.change(fromSetCookie: "__Host-sm_session=; HttpOnly; Max-Age=0"), .cleared)
        XCTAssertEqual(SessionCookie.change(fromSetCookie: "sm_ui_original=1; Path=/"), nil)
        XCTAssertEqual(SessionCookie.change(fromSetCookie: "a=b; Path=/, __Host-sm_session=abc; Secure"), .set("abc"))
        let t = FakeTransport { _ in .init(status: 200, body: json(#"{"ok":true}"#), headers: ["Set-Cookie": "__Host-sm_session=NEW; HttpOnly; SameSite=Strict; Path=/; Max-Age=2592000; Secure"]) }
        let c = makeClient(t, token: nil)
        _ = try await c.send(API.login(username: "admin", password: "correct horse"))
        XCTAssertEqual(c.session.token(), "NEW")
    }

    func testConfirmPasswordRetriesOnce() async throws {
        let confirmed = Locked(false)
        let t = FakeTransport { req in
            switch req.url!.path {
            case "/api/auth/confirm":
                if req.bodyString.contains("right") { confirmed.set(true); return .init(status: 200, body: json(#"{"ok":true}"#)) }
                return .init(status: 401, body: json(#"{"error":"That password is not right."}"#))
            default:
                return confirmed.get() ? .init(status: 200, body: try! apiFixture("vehicle-add.json"))
                    : .init(status: 403, body: json(#"{"error":"Enter your password to confirm this change.","code":"CONFIRM_PASSWORD"}"#))
            }
        }
        let c = makeClient(t)
        let asked = Locked<[Bool]>([])
        await c.setConfirmProvider { retry in
            asked.update { $0.append(retry) }
            return asked.get().count == 1 ? "wrong" : "right"
        }
        let v = try await c.send(API.addVehicle(VehicleBody(name: "Model Y", source: .folder("/x"))))
        XCTAssertEqual(v.id, "model-y")
        XCTAssertEqual(asked.get(), [false, true])
        XCTAssertEqual(t.requests.map { $0.url!.path }, ["/api/vehicles", "/api/auth/confirm", "/api/auth/confirm", "/api/vehicles"])
    }

    func testConfirmThreeWrongOrCancel() async throws {
        let t = FakeTransport { req in
            req.url!.path == "/api/auth/confirm" ? .init(status: 401, body: json(#"{"error":"That password is not right."}"#))
                : .init(status: 403, body: json(#"{"error":"x","code":"CONFIRM_PASSWORD"}"#))
        }
        let c = makeClient(t)
        let n = Locked(0)
        await c.setConfirmProvider { _ in n.update { $0 += 1 }; return "nope" }
        do { _ = try await c.send(API.deleteVehicle("v")); XCTFail() } catch let e as APIError {
            XCTAssertEqual(e, .notConfirmed)
            XCTAssertEqual(e.message, "Not changed: the password was not confirmed.")
        }
        XCTAssertEqual(n.get(), 3)
        await c.setConfirmProvider { _ in nil }
        do { _ = try await c.send(API.deleteVehicle("v")); XCTFail() } catch let e as APIError { XCTAssertEqual(e, .notConfirmed) }
    }

    func testConfirmOtherErrorEndsTheAttempt() async throws {
        let t = FakeTransport { req in
            req.url!.path == "/api/auth/confirm" ? .init(status: 429, body: json(#"{"error":"Too many attempts. Try again in 30 s."}"#))
                : .init(status: 403, body: json(#"{"error":"x","code":"CONFIRM_PASSWORD"}"#))
        }
        let c = makeClient(t)
        let n = Locked(0)
        await c.setConfirmProvider { _ in n.update { $0 += 1 }; return "pw" }
        do { _ = try await c.send(API.deleteVehicle("v")); XCTFail() } catch let e as APIError {
            XCTAssertEqual(e.message, "Too many attempts. Try again in 30 s.")
        }
        XCTAssertEqual(n.get(), 1)
    }

    func testConcurrentConfirmsShareOnePrompt() async throws {
        let confirmed = Locked(false)
        let t = FakeTransport { req in
            if req.url!.path == "/api/auth/confirm" { confirmed.set(true); return .init(status: 200, body: json(#"{"ok":true}"#)) }
            return confirmed.get() ? .init(status: 200, body: json(#"{"ok":true}"#)) : .init(status: 403, body: json(#"{"error":"x","code":"CONFIRM_PASSWORD"}"#))
        }
        let c = makeClient(t)
        let prompts = Locked(0)
        await c.setConfirmProvider { _ in
            prompts.update { $0 += 1 }
            try? await Task.sleep(nanoseconds: 50_000_000)
            return "pw"
        }
        async let a = c.send(API.deleteVehicle("a"))
        async let b = c.send(API.deleteVehicle("b"))
        _ = try await (a, b)
        XCTAssertEqual(prompts.get(), 1)
    }

    func testConverterEnvelopes() async throws {
        let t = FakeTransport { req in
            req.url!.path.hasSuffix("/start") ? .init(status: 200, body: try! apiFixture("converter-start-refused.json"))
                : .init(status: 200, body: try! apiFixture("converter-getCapabilities.json"))
        }
        let c = makeClient(t)
        let caps = try await c.invoke(API.invoke("getCapabilities", clientID: "cid", as: ConverterCapabilities.self))
        guard case .value(let v) = caps else { return XCTFail() }
        XCTAssertNotNil(v?.replacement)
        let refused = try await c.invoke(API.invoke("start", clientID: "cid", payload: json("{}"), as: ConverterStartAnswer.self))
        XCTAssertEqual(refused, .failure(ConverterError(code: "PATH_NOT_ALLOWED")))
        XCTAssertEqual(t.requests[0].value(forHTTPHeaderField: "X-Client-Id"), "cid")
        // an empty payload sends no body and no Content-Type, as the web
        XCTAssertNil(t.requests[0].httpBody)
        XCTAssertNil(t.requests[0].value(forHTTPHeaderField: "Content-Type"))
    }

    func testBodyLimits() async throws {
        let t = FakeTransport { _ in .init(status: 200, body: json("{}")) }
        let c = makeClient(t)
        let big = Data(repeating: 0x20, count: 256 * 1024 + 1)
        do { _ = try await c.send(Endpoint<OKAnswer>(.post, "/api/converter/invoke/start", body: big)); XCTFail() } catch let e as APIError { XCTAssertEqual(e, .bodyTooLarge) }
        _ = try await c.send(Endpoint<OKAnswer>(.post, "/api/settings/x", body: big))
        do { _ = try await c.send(Endpoint<OKAnswer>(.post, "/api/settings/x", body: Data(repeating: 0x20, count: 1024 * 1024 + 1))); XCTFail() } catch let e as APIError { XCTAssertEqual(e, .bodyTooLarge) }
    }

    func testTimeouts() {
        XCTAssertEqual(API.stats().timeout.seconds, 10)
        XCTAssertEqual(API.logs().timeout.seconds, 15)
        XCTAssertEqual(API.vehicles().timeout.seconds, 30)
        XCTAssertEqual(API.invoke("runBenchmark", clientID: "c", as: JSONValue.self, timeout: .benchmark).timeout.seconds, 6 * 3600)
    }
}

extension Data {
    /// the "body" of an error fixture ({status, body})
    func bodyOnly() throws -> Data {
        let o = try JSON.decode([String: JSONValue].self, from: self)
        return try JSON.encode(o["body"] ?? .null)
    }
}
