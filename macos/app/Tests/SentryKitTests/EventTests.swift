import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
import XCTest
@testable import SentryKit

final class SSEParserTests: XCTestCase {
    func parseAll(_ chunks: [Data]) -> [SSEEvent] {
        var p = SSEParser()
        return chunks.flatMap { p.feed($0) }
    }

    func testRealEventsStream() throws {
        let data = try apiFixture("events.sse")
        let events = parseAll([data])
        XCTAssertEqual(events.first, SSEEvent(event: "hello", data: "{}"))
        let changed = events.filter { $0.event == "changed" }
        XCTAssertFalse(changed.isEmpty)
        let topics = changed.compactMap { $0.decode(ChangedEvent.self)?.topics }.flatMap { $0 }
        XCTAssertTrue(topics.contains("bin"))
        XCTAssertTrue(topics.contains("inventory"))
        // the same events whichever way the bytes are cut
        for size in [1, 2, 3, 7, 64] {
            let chunks = stride(from: 0, to: data.count, by: size).map { data.subdata(in: $0..<min($0 + size, data.count)) }
            XCTAssertEqual(parseAll(chunks), events, "chunks of \(size)")
        }
    }

    func testRealConverterStream() throws {
        let events = parseAll([try apiFixture("converter-events.sse")])
        XCTAssertEqual(events.first?.event, "hello")
        let progress = events.filter { $0.event == "converter:progress" }.compactMap { $0.decode(ConverterProgressEvent.self) }
        XCTAssertFalse(progress.isEmpty)
        XCTAssertNotNil(progress.first?.snapshot?.config)
        XCTAssertEqual(progress.first?.progressOnly, false)
    }

    func testLineEndingsAndFields() {
        let crlf = parseAll([json("event: a\r\ndata: 1\r\n\r\n")])
        let cr = parseAll([json("event: a\rdata: 1\r\r")])
        let lf = parseAll([json("event: a\ndata: 1\n\n")])
        XCTAssertEqual(crlf, [SSEEvent(event: "a", data: "1")])
        XCTAssertEqual(cr, crlf)
        XCTAssertEqual(lf, crlf)
        // a CRLF cut between chunks is one line end
        XCTAssertEqual(parseAll([json("data: x\r"), json("\n\r"), json("\n")]), [SSEEvent(data: "x")])
        // multi-line data, comments, no space after the colon, a field with no colon
        var p = SSEParser()
        let out = p.feed(json(": ping\n\ndata:one\ndata: two\nid: 7\nretry: 3000\nevent\n\n"))
        XCTAssertEqual(out, [SSEEvent(event: "message", data: "one\ntwo", id: "7")])
        XCTAssertEqual(p.comments, 1)
        XCTAssertEqual(p.retryMs, 3000)
        // a blank line with no data dispatches nothing; a BOM is dropped
        XCTAssertEqual(parseAll([Data([0xef, 0xbb, 0xbf]) + json("event: hello\ndata: {}\n\n\n\n")]), [SSEEvent(event: "hello", data: "{}")])
        // an event without its blank line is not sent
        XCTAssertEqual(parseAll([json("data: half")]), [])
    }
}

/// Plays canned connections: each open takes the next script.
final class ScriptedConnector: StreamConnector, @unchecked Sendable {
    enum Step { case status(Int), bytes(String), end, hang }
    private let scripts: Locked<[[Step]]>
    let opened = Locked(0)
    let requests = Locked<[URLRequest]>([])
    private let cancelled = Locked<[StreamHandler]>([])

    init(_ scripts: [[Step]]) { self.scripts = Locked(scripts) }

    struct Conn: StreamConnection {
        let onCancel: @Sendable () -> Void
        func cancel() { onCancel() }
    }

    func open(_ request: URLRequest, handler: StreamHandler) -> StreamConnection {
        opened.update { $0 += 1 }
        requests.update { $0.append(request) }
        let steps = scripts.update { s -> [Step] in s.isEmpty ? [.hang] : s.removeFirst() }
        let done = Locked(false)
        let finish: @Sendable () -> Void = { if done.update({ d -> Bool in let w = d; d = true; return !w }) { handler.complete(nil) } }
        DispatchQueue.global().async {
            for s in steps {
                switch s {
                case .status(let c): handler.response(c)
                case .bytes(let b): handler.data(Data(b.utf8))
                case .end: finish(); return
                case .hang: return
                }
            }
        }
        return Conn(onCancel: finish)
    }
}

final class EventStreamTests: XCTestCase {
    func collect(_ s: EventStream, until: @escaping (StreamEvent) -> Bool) async -> [StreamEvent] {
        var out: [StreamEvent] = []
        for await e in s.events {
            out.append(e)
            if until(e) { break }
        }
        return out
    }

    let req: @Sendable () -> URLRequest = { URLRequest(url: URL(string: "https://127.0.0.1:9000/api/events")!) }

    func testHelloEventsAndReconnectWithBackoff() async throws {
        let clock = ManualClock()
        let c = ScriptedConnector([
            [.status(200), .bytes("event: hello\ndata: {}\n\nevent: changed\ndata: {\"topics\":[\"jobs\"]}\n\n"), .end],
            [.status(503), .end],
            [.status(503), .end],
            [.status(200), .bytes("event: hello\ndata: {}\n\n"), .hang],
        ])
        let s = EventStream(request: req, connector: c, clock: clock)
        await s.start()
        var seen: [StreamEvent] = []
        var it = s.events.makeAsyncIterator()
        // first connection
        while let e = await it.next() { seen.append(e); if case .event = e { break } }
        XCTAssertEqual(seen, [.state(.connecting), .state(.connected), .hello(reconnect: false), .event(SSEEvent(event: "changed", data: #"{"topics":["jobs"]}"#))])
        // closed after a good open: 1 s; then two 503s: 2 s and 4 s
        while c.opened.get() < 4 {
            clock.advance(0.5)
            try await Task.sleep(nanoseconds: 3_000_000)
        }
        var later: [StreamEvent] = []
        while let e = await it.next() { later.append(e); if case .hello = e { break } }
        XCTAssertEqual(clock.sleeps.filter { $0 < 40 }, [1, 2, 4])
        XCTAssertEqual(later.last, .hello(reconnect: true))
        XCTAssertTrue(later.contains(.state(.reconnecting)))
        XCTAssertEqual(c.opened.get(), 4)
        await s.stop()
    }

    func testBackoffNumbers() {
        var b = Backoff()
        XCTAssertEqual((0..<7).map { _ in b.next() }, [1, 2, 4, 8, 16, 30, 30])
        b.reset()
        XCTAssertEqual(b.next(), 1)
    }

    func testUnauthorizedStops() async {
        let c = ScriptedConnector([[.status(401), .end]])
        let s = EventStream(request: req, connector: c, clock: ManualClock())
        await s.start()
        let out = await collect(s) { $0 == .state(.stopped) }
        XCTAssertTrue(out.contains(StreamEvent.unauthorized))
        XCTAssertEqual(c.opened.get(), 1)
    }

    func testSilentStreamIsClosedAfter45s() async throws {
        let clock = ManualClock()
        let c = ScriptedConnector([[.status(200), .bytes("event: hello\ndata: {}\n\n"), .hang], [.status(200), .hang]])
        let s = EventStream(request: req, connector: c, clock: clock)
        await s.start()
        var it = s.events.makeAsyncIterator()
        while let e = await it.next() { if case .hello = e { break } }
        while clock.pendingSleepers == 0 { try await Task.sleep(nanoseconds: 2_000_000) }
        XCTAssertEqual(clock.sleeps.last ?? 0, 45, accuracy: 0.01)
        clock.advance(44)
        try await Task.sleep(nanoseconds: 20_000_000)
        XCTAssertEqual(c.opened.get(), 1, "not yet dead at 44 s")
        clock.advance(1)
        // closed and, after the 1 s backoff, opened again
        while c.opened.get() < 2 {
            clock.advance(0.5)
            try await Task.sleep(nanoseconds: 3_000_000)
        }
        XCTAssertEqual(c.opened.get(), 2)
        await s.stop()
    }

    func testConverterStreamSubscribesAndCatchesUp() async throws {
        let t = FakeTransport { req in
            req.url!.path.hasSuffix("getCurrent") ? .init(status: 200, body: try! apiFixture("converter-getCurrent-done.json"))
                : .init(status: 200, body: try! apiFixture("converter-subscribeProgress.json"))
        }
        let client = makeClient(t)
        let progress = try String(decoding: apiFixture("converter-events.sse"), as: UTF8.self)
        let clock = ManualClock()
        let c = ScriptedConnector([[.status(200), .bytes(progress), .end], [.status(200), .bytes("event: hello\ndata: {}\n\n"), .hang]])
        let cs = ConverterStream(client: client, connector: c, clientID: "abc", clock: clock)
        await cs.start()
        var updates: [ConverterStream.Update] = []
        var it = cs.updates.makeAsyncIterator()
        while let u = await it.next() {
            updates.append(u)
            if case .progress = u, updates.filter({ if case .progress = $0 { return true }; return false }).count == 5 { break }
        }
        while c.opened.get() < 2 {
            clock.advance(0.5)
            try await Task.sleep(nanoseconds: 3_000_000)
        }
        while let u = await it.next() { updates.append(u); if case .current = u { break } }
        let paths = t.requests.map { $0.url!.path }
        XCTAssertEqual(paths.filter { $0.hasSuffix("subscribeProgress") }.count, 2, "after every hello")
        XCTAssertEqual(paths.filter { $0.hasSuffix("getCurrent") }.count, 1, "only after a reconnect")
        XCTAssertTrue(t.requests.allSatisfy { $0.value(forHTTPHeaderField: "X-Client-Id") == "abc" })
        XCTAssertEqual(c.requests.get().first?.url?.query, "client=abc")
        if case .current(let s)? = updates.last { XCTAssertEqual(s?.state, "completed") } else { XCTFail() }
        await cs.stop()
        XCTAssertTrue(t.requests.last!.url!.path.hasSuffix("unsubscribeProgress"))
    }

    func testTopicRouting() {
        XCTAssertEqual(TopicRouter.global(["inventory", "bin"]), [.vehicles])
        XCTAssertEqual(TopicRouter.global(["jobs", "notifications", "interface", "unknown"]), [.jobs, .notifications, .interface])
        XCTAssertTrue(TopicRouter.pageReloads(.footage, topics: ["inventory"]))
        XCTAssertFalse(TopicRouter.pageReloads(.footage, topics: ["jobs"]))
        XCTAssertTrue(TopicRouter.pageReloads(.home, topics: ["bin"]))
        XCTAssertTrue(TopicRouter.pageReloads(.policies, topics: ["inventory"]))
        XCTAssertFalse(TopicRouter.pageReloads(.viewer, topics: ["inventory", "jobs"]))
    }

    func testDebouncer() async throws {
        let clock = ManualClock()
        let d = TopicDebouncer(clock: clock, delay: 0.25)
        await d.add(["jobs"])
        await d.add(["bin"])
        while clock.pendingSleepers < 1 { try await Task.sleep(nanoseconds: 1_000_000) }
        clock.advance(0.25)
        var it = d.batches.makeAsyncIterator()
        let batch = await it.next()
        XCTAssertEqual(batch, ["jobs", "bin"])
    }
}
