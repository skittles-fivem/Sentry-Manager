import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
import XCTest
@testable import SentryKit

func services(_ t: HTTPTransport, prefs: PrefsStore = MemoryPrefs(), clock: AppClock = ManualClock(), now: Double = 1_793_379_100_000) -> Services {
    Services(api: makeClient(t), clock: clock, prefs: prefs, nowMs: { now })
}

/// Every GET answers from the dumped fixtures.
let fixtureRoutes: [String: String] = [
    "/api/vehicles": "vehicles.json", "/api/notifications": "notifications-open.json", "/api/jobs": "jobs.json",
    "/api/info": "info.json", "/api/settings": "settings.json", "/api/ui-packs": "ui-packs.json", "/api/updates": "updates.json",
    "/api/auth/me": "auth-me.json", "/api/stats": "stats-after.json", "/api/stats/history": "stats-history.json",
    "/api/logs": "logs.json", "/api/security": "security.json", "/api/system": "system.json", "/api/bin": "bin.json",
    "/api/nodes": "nodes.json", "/api/vehicles/model-y/days": "days.json", "/api/vehicles/model-y/recordings": "recordings.json",
    "/api/telemetry": "telemetry.json", "/api/vehicles/model-y/policy": "policy.json",
]

final class ConverterTrackerTests: XCTestCase {
    func events() throws -> [ConverterProgressEvent] {
        var p = SSEParser()
        return p.feed(try apiFixture("converter-events.sse")).filter { $0.event == "converter:progress" }.compactMap { $0.decode(ConverterProgressEvent.self) }
    }

    func progressOnly(_ id: String, _ rev: Double, fps: Double) -> ConverterProgressEvent {
        var s = ConverterSnapshot()
        s.id = id; s.revision = rev; s.progress = .object(["fps": .number(fps), "waitingOnNode": .number(2), "encodingNow": .number(1)])
        return ConverterProgressEvent(jobId: id, revision: rev, snapshot: s, progressOnly: true)
    }

    func testRevisionsAndMerging() throws {
        let evs = try events()
        var t = ConverterTracker()
        for e in evs { t.handle(e) }
        let last = try XCTUnwrap(evs.last)
        XCTAssertEqual(t.latestRevision, last.revision)
        // an older revision is ignored
        XCTAssertFalse(t.handle(evs[0]))
        // a progress-only event merges and keeps the rest
        let id = try XCTUnwrap(last.jobId)
        XCTAssertTrue(t.handle(progressOnly(id, last.revision! + 1, fps: 41.5)))
        XCTAssertEqual(t.snapshot?.config, last.snapshot?.config)
        XCTAssertEqual(t.snapshot?.progress?["fps"]?.number, 41.5)
        XCTAssertEqual(t.fps[id], 41.5)
        XCTAssertEqual(t.nodeWaiting[id]?.count, 2)
        // a full snapshot replaces it and drops progress
        var full = last
        full.revision = last.revision! + 2
        full.snapshot?.revision = full.revision
        XCTAssertTrue(t.handle(full))
        XCTAssertNil(t.snapshot?.progress)
    }

    func testOtherJobs() throws {
        let evs = try events()
        var t = ConverterTracker()
        t.handle(evs[0]) // a running (waiting) job
        XCTAssertFalse(t.handle(progressOnly("other", 1, fps: 1)), "another job's progress is ignored")
        var other = evs[0]
        other.jobId = "other"; other.snapshot?.id = "other"; other.revision = 1; other.snapshot?.state = "waiting"
        XCTAssertFalse(t.handle(other), "a running job is not replaced")
        // once it finished, another job's full snapshot takes over
        var done = evs.last!
        done.snapshot?.state = "completed"
        done.revision = 999
        t.handle(done)
        XCTAssertTrue(t.handle(other))
        XCTAssertEqual(t.snapshot?.id, "other")
    }

    func testStartBuffersUnknownJobs() throws {
        let evs = try events()
        var t = ConverterTracker()
        t.startBegan()
        XCTAssertTrue(t.isStarting)
        for e in evs { XCTAssertFalse(t.handle(e)) }
        let id = evs[0].jobId!
        XCTAssertFalse(t.handle(progressOnly(id, 9999, fps: 30)))
        // at most 8 job ids are held
        for i in 0..<10 { t.handle(progressOnly("x\(i)", 1, fps: 1)) }
        t.startAnswered(jobID: id, config: evs[0].snapshot?.config, now: 1000)
        XCTAssertFalse(t.isStarting)
        // the newest full one, then the newest progress-only one
        XCTAssertEqual(t.snapshot?.revision, evs.last!.revision)
        XCTAssertEqual(t.latestRevision, 9999)
        XCTAssertEqual(t.snapshot?.progress?["fps"]?.number, 30)
    }

    func testStartPlaceholderAndCounts() throws {
        var t = ConverterTracker()
        t.startBegan()
        t.startAnswered(jobID: "j", config: ConverterConfig(codec: "h265"), now: 5)
        XCTAssertEqual(t.snapshot?.state, "discovering")
        XCTAssertEqual(t.snapshot?.createdAt, 5)
        XCTAssertEqual(t.itemCount, 0)
        let done = try JSON.decode(ConverterEnvelope<ConverterSnapshot>.self, from: try apiFixture("converter-getCurrent-done.json")).value!
        var big = done
        big.itemsTruncated = true; big.itemCount = 5000; big.itemStateCounts = ["completed": 4000, "failed": 1000]
        var t2 = ConverterTracker()
        t2.apply(big, revision: big.revision)
        XCTAssertEqual(t2.itemCount, 5000, "counts from the server's fields")
        XCTAssertEqual(t2.count("failed"), 1000)
        var t3 = ConverterTracker()
        t3.apply(done, revision: done.revision)
        XCTAssertEqual(t3.count("failed"), done.items!.filter { $0.state == "failed" }.count)
    }

    func testThroughputMeter() {
        var m = ThroughputMeter()
        var s = ConverterSnapshot()
        s.id = "a"; s.state = "encoding"; s.createdAt = 0
        XCTAssertNil(m.pace(snapshot: s, done: 0, remaining: 10, now: 0).perMinute)
        // whole-run average while the window is short
        let p1 = m.pace(snapshot: s, done: 2, remaining: 8, now: 60_000)
        XCTAssertEqual(p1.perMinute ?? 0, 2, accuracy: 1e-9)
        XCTAssertEqual(p1.remainingMs ?? 0, 4 * 60_000, accuracy: 1e-6)
        // then the recent window
        let p2 = m.pace(snapshot: s, done: 8, remaining: 2, now: 120_000)
        XCTAssertEqual(p2.perMinute ?? 0, 4, accuracy: 1e-9, "8 files in the 2 minutes since the first sample")
        // paused: no pace
        s.state = "paused"
        XCTAssertNil(m.pace(snapshot: s, done: 8, remaining: 2, now: 130_000).perMinute)
        // completed: the run's average over working time
        s.state = "completed"; s.updatedAt = 240_000; s.pausedMs = 0
        XCTAssertEqual(m.pace(snapshot: s, done: 8, remaining: 0, now: 999_999).perMinute ?? 0, 2, accuracy: 1e-9)
        // a new job starts over
        var n = ConverterSnapshot(); n.id = "b"; n.state = "encoding"; n.createdAt = 500_000
        XCTAssertNil(m.pace(snapshot: n, done: 1, remaining: 1, now: 505_000).perMinute)
        XCTAssertEqual(ConverterStates.workingMs(s, at: 0), 240_000)
    }
}

final class PageModelTests: XCTestCase {
    @MainActor func testShellStartupAndBadges() async throws {
        let t = fixtureServer(fixtureRoutes)
        let prefs = MemoryPrefs([SkinPrefs.selectedVehicle: "gone"])
        let shell = ShellModel(services: services(t, prefs: prefs))
        await shell.start()
        XCTAssertEqual(shell.deployment, "macos")
        XCTAssertEqual(shell.selectedVehicle?.id, "model-y")
        XCTAssertEqual(prefs.value(SkinPrefs.selectedVehicle), "model-y", "a vehicle that is gone falls back to the first")
        XCTAssertEqual(shell.openCount, 1)
        XCTAssertEqual(shell.unreadOpenCount, 1)
        XCTAssertFalse(shell.anyOpenCritical)
        XCTAssertEqual(shell.runningJobs, 0)
        XCTAssertFalse(shell.conversionRunning)
        XCTAssertEqual(shell.interface, .skin(.builtin))
        XCTAssertEqual(shell.binItems, 0, "dumped before anything was moved to the bin")
        XCTAssertEqual(shell.pickerEntries.first?.sub, "3 recordings")
        // startup order: settings first
        XCTAssertEqual(t.requests.first?.url?.path, "/api/settings")
        let before = t.requests.count
        await shell.apply(topics: ["inventory", "jobs"])
        let paths = t.requests.dropFirst(before).map { $0.url!.path }.sorted()
        XCTAssertEqual(paths, ["/api/jobs", "/api/vehicles"])
        XCTAssertEqual(shell.pulse, 1)
        // a busy hold keeps live redraws until it ends
        shell.busyHold = true
        let held = t.requests.count
        await shell.apply(topics: ["bin"])
        XCTAssertEqual(t.requests.count, held)
        XCTAssertEqual(shell.reduceMotion(systemReduceMotion: true), true)
        XCTAssertEqual(shell.reduceMotion(systemReduceMotion: false), false)
    }

    func testPickerEntry() throws {
        var v = try JSON.decode(VehiclesAnswer.self, from: try apiFixture("vehicles.json")).vehicles![0]
        XCTAssertEqual(VehiclePickerEntry(v).dot, .green)
        v.share?.healthy = false
        XCTAssertEqual(VehiclePickerEntry(v).dot, .red)
        v.share?.healthy = true; v.share?.writable = false
        XCTAssertEqual(VehiclePickerEntry(v).dot, .amber)
        v.share?.writable = true
        XCTAssertEqual(VehiclePickerEntry(v).dot, .green)
        v.share = nil; v.inventory = nil; v.scanning = true
        XCTAssertEqual(VehiclePickerEntry(v).dot, .none)
        XCTAssertEqual(VehiclePickerEntry(v).sub, "scanning…")
        v.scanning = false
        XCTAssertEqual(VehiclePickerEntry(v).sub, "not scanned yet")
    }

    @MainActor func testSignIn() async throws {
        let t = FakeTransport { req in
            switch req.url!.path {
            case "/api/auth/me": return .init(status: 200, body: try! apiFixture("auth-me-needs-setup.json"))
            case "/api/auth/setup": return .init(status: 403, body: try! apiFixture("setup-403-wrong-code.json").bodyOnly())
            default: return .init(status: 429, body: json(#"{"error":"Too many attempts. Try again in 3 s."}"#))
            }
        }
        let clock = ManualClock()
        let m = SignInModel(services: services(t, clock: clock))
        await m.load()
        XCTAssertEqual(m.mode, .create)
        m.password = "short"; m.confirmPassword = "other"
        XCTAssertEqual(m.validateCreate(), "The passwords do not match.")
        m.confirmPassword = "short"
        XCTAssertEqual(m.validateCreate(), "Use a password of at least 8 characters.")
        m.password = "correct horse"; m.confirmPassword = "correct horse"; m.username = "admin"; m.setupCode = "AAAAA-AAAAA-AAAAA"
        let ok = await m.submit()
        XCTAssertFalse(ok)
        XCTAssertTrue(m.error?.hasPrefix("That setup code is not right.") == true, m.error ?? "")
        XCTAssertEqual(SignInModel.retrySeconds("Too many attempts. Try again in 30 s."), 30)
        XCTAssertNil(SignInModel.retrySeconds("Another sign-in is being checked. Try again in a moment."))
    }

    @MainActor func testSignInInsecure() async {
        let t = FakeTransport { _ in .init(status: 200, body: json(#"{"user":null,"needsSetup":false,"secure":false,"fingerprint":null,"deployment":"macos"}"#)) }
        let m = SignInModel(services: services(t))
        await m.load()
        XCTAssertEqual(m.mode, .insecure)
        XCTAssertFalse(m.canSubmit)
        let sent = await m.submit()
        XCTAssertFalse(sent)
        XCTAssertEqual(t.requests.count, 1, "no password goes over plain HTTP")
    }

    @MainActor func testFootageConfirms() async throws {
        let t = FakeTransport { req in
            if req.url!.path.hasSuffix("/trash-days") {
                return .init(status: 200, body: json(#"{"recordings":3,"bytes":3145728,"protected":1,"days":2,"oldest":"2025-06-01","started":false}"#))
            }
            return fixtureServer(fixtureRoutes).sendSync(req)
        }
        let m = FootageModel(services: services(t))
        m.vehicleID = "model-y"
        await m.load()
        XCTAssertEqual(m.days.map(\.day), ["2025-06-03", "2025-06-02", "2025-06-01"])
        await m.toggle(day: "2025-06-01")
        let ids = m.dayRecordings["2025-06-01"]!.map(\.id)
        m.selected = Set(ids)
        let c = m.trashConfirm()
        XCTAssertEqual(ids.count, 1, "the two minutes of 08:00 are one recording")
        XCTAssertEqual(c.title, "Move 1 recording to the Recycle Bin?")
        XCTAssertTrue(c.paragraphs[0].hasSuffix("moves to the Recycle Bin on the same share. Nothing is deleted: restore it any time, or delete it forever from the Recycle Bin."))
        guard case .confirm(let d) = try await m.previewDayDelete("2025-06-03", through: true) else { return XCTFail() }
        XCTAssertEqual(d.title, "Delete through this date?")
        XCTAssertEqual(d.paragraphs[0], "3 recordings from Tuesday, Jun 3, 2025 and every older day, 3.0 MiB over 2 days, move to the Recycle Bin.")
        XCTAssertEqual(d.paragraphs[1], "Nothing is deleted for good until you empty the Recycle Bin; you can restore it until then. 1 protected recording stays.")
        XCTAssertEqual(d.ok, "Delete through this date")
        XCTAssertTrue(d.danger)
        XCTAssertEqual(Confirms.trashDaysNothing(try JSON.decode(TrashDaysAnswer.self, from: json(#"{"recordings":0,"protected":2}"#))), "Everything there is protected: nothing to move.")
        XCTAssertTrue(t.requests.contains { $0.url!.path.hasSuffix("/trash-days") && $0.bodyString.contains(#""preview":true"#) })
    }

    @MainActor func testVehicleForm() async throws {
        let v = try JSON.decode(Vehicle.self, from: json(#"{"id":"car","name":"Car","timezone":"UTC","source":{"type":"smb","share":"//nas/TeslaCam","username":"tesla","domain":"","version":"3.1.1","encrypt":true,"port":null,"passwordSet":true}}"#))
        let t = FakeTransport { _ in .init(status: 200, body: json(#"{"ok":false,"stage":"connect","error":"The NAS did not answer.","hint":"Check the address."}"#)) }
        let f = VehicleFormModel(services: services(t), editing: v, defaultSMBVersion: "3.1.1", defaultTimezone: "UTC")
        XCTAssertTrue(f.sourceUnchanged, "a rename alone keeps the source")
        f.password = "new"
        XCTAssertFalse(f.sourceUnchanged)
        f.password = ""; f.port = "445"
        XCTAssertFalse(f.sourceUnchanged)
        f.port = ""; f.encrypt = false
        XCTAssertFalse(f.sourceUnchanged)
        await f.testConnection()
        XCTAssertEqual(f.testText, "Could not connect: The NAS did not answer. Check the address.")
        let body = t.requests[0].bodyString
        XCTAssertTrue(body.contains(#""vehicleId":"car""#) && body.contains(#""port":null"#) && !body.contains("password"), body)
        XCTAssertEqual(VehicleFormModel.testResultText(try JSON.decode(TestSMBAnswer.self, from: json(#"{"ok":true,"footage":".","writable":true}"#))),
                       "Connected: the user name and password work. Tesla footage found.")
        XCTAssertEqual(VehicleFormModel.testResultText(try JSON.decode(TestSMBAnswer.self, from: json(#"{"ok":true,"footage":null,"writable":false}"#))),
                       "Connected: the user name and password work. No TeslaCam folder found yet (empty share, or the footage is deeper than 3 folders). Read-only for this account: the Recycle Bin, Protect and replacing originals need write access.")
        XCTAssertEqual(VehicleFormModel.testResultText(try JSON.decode(TestSMBAnswer.self, from: json(#"{"ok":false,"stage":"details","error":"Enter the share."}"#))), "Enter the share.")
    }

    @MainActor func testNodesUseAtOnceWritesTheLanes() async throws {
        let t = FakeTransport { req in
            if req.url!.path == "/api/nodes" {
                return .init(status: 200, body: json(#"{"nodes":[{"id":"n1","name":"Attic","defaultLane":"node:n1:pci:0000:01:00.0","defaultDeviceName":"RTX 3060"},{"id":"n2","name":"Den"}],"worker":{"enabled":false}}"#))
            }
            return .init(status: 200, body: json(#"{"ok":true}"#))
        }
        let prefs = MemoryPrefs()
        let m = NodesModel(services: services(t, prefs: prefs))
        m.lanes.set("node:n1", 3)
        await m.load()
        let toast = try await m.useAtOnce("n1", 4)
        XCTAssertEqual(toast, "RTX 3060 on Attic now converts 4 at once; its row under Encoder is set to 4 too.")
        XCTAssertEqual(m.lanes.lanes(), ["node:n1:pci:0000:01:00.0": 4], "the whole-node row is replaced")
        let toast2 = try await m.useAtOnce("n2", 2)
        XCTAssertEqual(toast2, "The node now converts 2 at once; its Encoder row is set to 2 too.")
        XCTAssertEqual(m.lanes.lanes()["node:n2"], 2)
        XCTAssertTrue(t.requests.contains { $0.url!.path == "/api/nodes/n1/slots" && $0.bodyString == #"{"slots":4}"# })
    }

    @MainActor func testJobsBinNotifications() async throws {
        let t = FakeTransport { req in
            switch req.url!.path {
            case "/api/jobs/clear": return .init(status: 200, body: json(req.bodyString.contains("preview") ? #"{"jobs":1}"# : #"{"cleared":1}"#))
            default: return fixtureServer(fixtureRoutes).sendSync(req)
            }
        }
        let s = services(t)
        let jobs = JobsModel(services: s)
        await jobs.load()
        XCTAssertFalse(jobs.jobs.isEmpty)
        jobs.status = "failed"
        XCTAssertTrue(jobs.shown.allSatisfy { $0.status == "failed" })
        jobs.status = nil; jobs.search = "convert"
        XCTAssertTrue(jobs.shown.allSatisfy { $0.title!.lowercased().contains("convert") })
        let c = try await jobs.clearPreview(vehicleName: "Model Y", manyVehicles: false)
        XCTAssertEqual(c?.paragraphs.first, "1 finished job (done, failed, cancelled or interrupted) leaves the list. Running, waiting and paused jobs stay.")
        XCTAssertTrue(t.requests.contains { $0.url!.path == "/api/jobs/clear" && $0.bodyString == #"{"preview":true,"vehicle":null}"# })

        let bin = BinModel(services: s)
        await bin.load()
        XCTAssertEqual(bin.items.count, 1)
        let item = bin.items[0]
        XCTAssertEqual(bin.deleteConfirm(item).paragraphs[0], "\(item.label!) (\(Format.bytes(item.bytes))) will be permanently deleted from the share. This cannot be undone.")
        XCTAssertEqual(bin.emptyConfirm(vehicleName: nil).paragraphs[0], "1 item (\(Format.bytes(item.bytes))) of all vehicles will be permanently deleted. This cannot be undone.")

        let notes = NotificationsModel(services: s)
        await notes.load()
        XCTAssertEqual(notes.shown.count, 1)
        notes.filter = .unread
        XCTAssertEqual(notes.shown.count, 1)
        XCTAssertEqual(NotificationsModel.newlyOpen(previous: [], current: notes.all).count, 1)
        XCTAssertEqual(NotificationsModel.newlyOpen(previous: notes.all, current: notes.all).count, 0)
    }

    @MainActor func testPolicies() async throws {
        let t = fixtureServer(fixtureRoutes)
        let m = PoliciesModel(services: services(t))
        let v = try JSON.decode(VehiclesAnswer.self, from: try apiFixture("vehicles.json")).vehicles!
        await m.load(vehicles: v)
        XCTAssertNotNil(m.policies["model-y"])
        XCTAssertEqual(m.runConfirm("model-y")?.title, "Run the policy now?")
        XCTAssertEqual(PoliciesModel.keepDays(from: ""), .some(nil))
        XCTAssertEqual(PoliciesModel.keepDays(from: " 30 "), .some(30))
        XCTAssertTrue(PoliciesModel.keepDays(from: "1.5") == nil)
    }

    @MainActor func testStatsAndLogs() async throws {
        let t = fixtureServer(fixtureRoutes)
        let m = OverviewModel(services: services(t), keep: 90)
        await m.stats.prefill()
        XCTAssertLessThanOrEqual(m.stats.series["cpu"]!.values.count, 90)
        await m.stats.refresh()
        XCTAssertEqual(m.stats.stats?.host?.os, "macOS")
        XCTAssertEqual(m.stats.series["cpu"]!.values.last ?? nil, m.stats.stats?.cpu?.usage)
        await m.logs.load()
        XCTAssertFalse(m.logs.lines.isEmpty)
        m.logs.text = "setup code"
        XCTAssertTrue(m.logs.shown.allSatisfy { ($0.message ?? "").lowercased().contains("setup code") })
        var r = RingBuffer(keep: 3)
        for i in 0..<5 { r.append(Double(i)) }
        XCTAssertEqual(r.values, [2, 3, 4])
    }

    @MainActor func testViewerLoadsADay() async throws {
        let detail = try apiFixture("recording.json")
        let t = FakeTransport { req in
            if req.url!.path.hasPrefix("/api/vehicles/model-y/recordings/") { return .init(status: 200, body: detail) }
            return fixtureServer(fixtureRoutes).sendSync(req)
        }
        let m = ViewerModel(services: services(t))
        await m.load(vehicleID: "model-y", day: "2025-06-01")
        XCTAssertEqual(m.state, .loaded)
        XCTAssertEqual(m.playlist?.layout, .hw4)
        XCTAssertEqual(m.tiles.count, 6)
        XCTAssertNotNil(m.telemetry)
        m.playerTime(0.5)
        XCTAssertNotNil(m.record)
        XCTAssertEqual(m.wallClock, "8:00:00 AM")
        m.clock24 = true
        XCTAssertEqual(m.wallClock, "08:00:00")
        XCTAssertEqual(m.speedText, String(DashboardMath.speed(Double(m.record!.speedMps), unit: .mph)))
        XCTAssertEqual(m.stepTarget(1), 0.5 + 1 / m.telemetry!.fps, accuracy: 1e-9)
        // the second load of the same clip comes from the cache
        let n = t.requests.filter { $0.url!.path == "/api/telemetry" }.count
        await m.loadTelemetry()
        XCTAssertEqual(t.requests.filter { $0.url!.path == "/api/telemetry" }.count, n)
        var lru = LRUCache<Int>(capacity: 2)
        lru.put("a", 1); lru.put("b", 2); _ = lru.get("a"); lru.put("c", 3)
        XCTAssertEqual(lru.keys, ["a", "c"])
    }

    @MainActor func testEncoderStartAndCommands() async throws {
        let t = FakeTransport { req in
            let name = req.url!.lastPathComponent
            switch name {
            case "start": return .init(status: 200, body: try! apiFixture("converter-start.json"))
            case "pause": return .init(status: 200, body: try! apiFixture("converter-getCurrent-running.json"))
            case "getCurrent": return .init(status: 200, body: try! apiFixture("converter-getCurrent-null.json"))
            case "getCapabilities": return .init(status: 200, body: try! apiFixture("converter-getCapabilities.json"))
            case "devices": return .init(status: 200, body: try! apiFixture("converter-devices.json"))
            case "getAuto": return .init(status: 200, body: try! apiFixture("converter-getAuto.json"))
            default: return .init(status: 200, body: try! apiFixture("converter-unknown.json"))
            }
        }
        let clock = ManualClock()
        let m = EncoderModel(services: services(t, clock: clock), clientID: "cid")
        await m.appear()
        m.disappear()
        XCTAssertEqual(m.devices?.devices?.last?.id, "cpu")
        XCTAssertNil(m.snapshot)
        let ok = await m.start(ConverterConfig(sourceRoot: "/a", codec: "h265"))
        XCTAssertTrue(ok)
        XCTAssertEqual(m.snapshot?.id, try JSON.decode(ConverterEnvelope<ConverterStartAnswer>.self, from: try apiFixture("converter-start.json")).value?.jobId)
        XCTAssertEqual(m.snapshot?.state, "discovering")
        await m.command("pause")
        let pause = try XCTUnwrap(t.requests.last)
        XCTAssertEqual(pause.bodyString, #"{"jobId":"\#(m.snapshot!.id!)"}"#)
        XCTAssertTrue(t.requests.allSatisfy { $0.value(forHTTPHeaderField: "X-Client-Id") == "cid" })
        // the stepper sends only the last value, 350 ms after the last change
        m.setConcurrency(3)
        m.setConcurrency(4)
        while clock.pendingSleepers < 1 { try await Task.sleep(nanoseconds: 1_000_000) }
        clock.advance(0.35)
        for _ in 0..<200 where !t.requests.contains(where: { $0.url!.lastPathComponent == "setConcurrency" }) { try await Task.sleep(nanoseconds: 2_000_000) }
        let sets = t.requests.filter { $0.url!.lastPathComponent == "setConcurrency" }
        XCTAssertEqual(sets.count, 1)
        XCTAssertTrue(sets[0].bodyString.contains(#""concurrency":4"#))
        // a refused command keeps the error code for the page's words
        _ = await m.runBenchmark(.object([:]))
        XCTAssertEqual(m.lastFailure?.code, "UNKNOWN_CHANNEL")
    }

    @MainActor func testMenuBar() throws {
        let m = MenuBarModel()
        let t0 = Date(timeIntervalSince1970: 1_000)
        XCTAssertEqual(m.statusText, "Stopped")
        m.didStart(at: t0)
        m.health(ok: false, launchdRunning: true, now: t0.addingTimeInterval(5))
        XCTAssertEqual(m.statusText, "Starting…")
        m.health(ok: true, launchdRunning: true, now: t0.addingTimeInterval(10))
        XCTAssertEqual(m.statusText, "Sentry Six Manager is running")
        m.health(ok: false, launchdRunning: false, now: t0.addingTimeInterval(20))
        XCTAssertEqual(m.statusText, "Stopped")
        // signed out: no count
        XCTAssertEqual(m.conversionsText, "Sign in to see conversions")
        XCTAssertNil(m.stopConfirm)
        m.update(jobs: try JSON.decode(JobsAnswer.self, from: fixture("api/jobs.json")).jobs ?? [])
        XCTAssertNotNil(m.conversions)
        m.update(jobs: [Job(id: 1, type: "conversion", status: "running"), Job(id: 2, type: "conversion", status: "done")])
        XCTAssertEqual(m.conversionsText, "Converting: 1 job")
        XCTAssertEqual(m.stopConfirm, "Stop Sentry Six Manager? 1 conversion is running. It pauses safely and continues when it starts again.")
        m.update(me: AuthMe(user: nil, needsSetup: true))
        XCTAssertTrue(m.needsSetup)
        XCTAssertNil(m.conversions)
    }
}

extension FakeTransport {
    /// answers a request synchronously (to chain fake servers)
    func sendSync(_ r: URLRequest) -> Answer {
        let sem = DispatchSemaphore(value: 0)
        let out = Locked<Answer>(Answer(status: 500, body: Data()))
        Task {
            if let (d, res) = try? await self.send(r) { out.set(Answer(status: res.statusCode, body: d)) }
            sem.signal()
        }
        sem.wait()
        return out.get()
    }
}
