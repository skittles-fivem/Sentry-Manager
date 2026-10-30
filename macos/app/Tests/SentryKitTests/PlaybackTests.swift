import Foundation
import XCTest
@testable import SentryKit

final class MediaTests: XCTestCase {
    func testContentRange() throws {
        // the server's own answer to Range: bytes=0-1
        let h = try JSON.decode([String: JSONValue].self, from: try apiFixture("media-range-headers.json"))
        XCTAssertEqual(h["status"]?.number, 206)
        let r = try XCTUnwrap(ContentRange.parse(h["content-range"]?.string))
        XCTAssertEqual(r.start, 0)
        XCTAssertEqual(r.end, 1)
        XCTAssertGreaterThan(r.total ?? 0, 1000)
        XCTAssertEqual(r.length, 2)
        XCTAssertEqual(ContentRange.parse("bytes */5000"), ContentRange(start: nil, end: nil, total: 5000))
        XCTAssertEqual(ContentRange.parse("bytes 10-19/*"), ContentRange(start: 10, end: 19, total: nil))
        XCTAssertNil(ContentRange.parse("bytes 5-2/10"))
        XCTAssertNil(ContentRange.parse("bytes 0-10/10"))
        XCTAssertNil(ContentRange.parse("items 0-1/2"))
        XCTAssertNil(ContentRange.parse(nil))
    }

    func testRangePlanner() {
        let mib: Int64 = 1024 * 1024
        XCTAssertEqual(RangePlanner.plan(offset: 0, length: 2, toEnd: false, total: 100), [0...1])
        XCTAssertEqual(RangePlanner.plan(offset: 0, length: 5 * mib, toEnd: false, total: 10 * mib),
                       [0...(2 * mib - 1), (2 * mib)...(4 * mib - 1), (4 * mib)...(5 * mib - 1)])
        // to the end of the file
        XCTAssertEqual(RangePlanner.plan(offset: mib, length: nil, toEnd: true, total: 6 * mib),
                       [mib...(3 * mib - 1), (3 * mib)...(5 * mib - 1), (5 * mib)...(6 * mib - 1)])
        // asked past the end: cut at the end
        XCTAssertEqual(RangePlanner.plan(offset: 90, length: 50, toEnd: false, total: 100), [90...99])
        XCTAssertEqual(RangePlanner.plan(offset: 100, length: 5, toEnd: false, total: 100), [])
        // total not known yet: one chunk
        XCTAssertEqual(RangePlanner.plan(offset: 0, length: nil, toEnd: true, total: nil), [0...(2 * mib - 1)])
        XCTAssertEqual(RangePlanner.header(0...1), "bytes=0-1")
    }

    func testAnswers() {
        XCTAssertEqual(MediaAnswer.classify(status: 206, askedRange: true), .data)
        XCTAssertEqual(MediaAnswer.classify(status: 200, askedRange: true), .fileChanged)
        XCTAssertEqual(MediaAnswer.classify(status: 200, askedRange: false), .data)
        XCTAssertEqual(MediaAnswer.classify(status: 401, askedRange: true), .signIn)
        XCTAssertEqual(MediaAnswer.classify(status: 403, askedRange: true).message, "Outside the vehicle shares.")
        XCTAssertEqual(MediaAnswer.classify(status: 404, askedRange: true).message, "This clip is no longer there")
        XCTAssertEqual(MediaAnswer.classify(status: 416, askedRange: true), .finished)
    }

    func testMediaURL() {
        let p = "/Users/Shared/SentrySixFixture/cars/Model Y/TeslaCam/RecentClips/2025-06-01_08-00-00-front.mp4"
        let u = MediaURL.asset(path: p)
        XCTAssertEqual(u.scheme, "sentry-media")
        XCTAssertEqual(MediaURL.apiPathAndQuery(for: u), API.mediaPath(p))
        XCTAssertNil(MediaURL.apiPathAndQuery(for: URL(string: "https://x/media?path=%2Fa")!))
        XCTAssertNil(MediaURL.apiPathAndQuery(for: URL(string: "sentry-media://media?path=relative")!))
        XCTAssertEqual(MediaURL.contentType(path: "a.MOV"), "com.apple.quicktime-movie")
        XCTAssertEqual(MediaURL.contentType(path: "a.mp4"), "public.mpeg-4")
    }
}

final class TelemetryTests: XCTestCase {
    func track() throws -> TelemetryTrack { TelemetryTrack(try JSON.decode(TelemetryAnswer.self, from: try apiFixture("telemetry.json"))) }

    /// the records dump-api.js wrote into the clip (seiRecord(i)), read back by the server
    func testRealTelemetryDecodes() throws {
        let t = try track()
        XCTAssertEqual(t.samples.count, 24)
        for (i, s) in t.samples.enumerated() {
            let r = s.record
            XCTAssertEqual(r.version, 1)
            XCTAssertEqual(r.gear, 1)
            XCTAssertEqual(r.frameSeqNo, UInt64(1000 + i))
            XCTAssertEqual(r.speedMps, Float(12.5 + Double(i) * 0.25))
            XCTAssertEqual(r.accelerator, Float(0.2 + Double(i % 5) * 0.1))
            XCTAssertEqual(r.steeringDeg, Float(-15 + Double(i) * 1.5))
            XCTAssertEqual(r.blinkerLeft, i % 6 < 3)
            XCTAssertFalse(r.blinkerRight)
            XCTAssertEqual(r.brake, i % 7 == 0)
            XCTAssertEqual(r.autopilot, i < 12 ? 0 : 2)
            XCTAssertEqual(r.latitude, 30.2672 + Double(i) * 0.00005)
            XCTAssertEqual(r.longitude, -97.7431 + Double(i) * 0.00004)
            XCTAssertEqual(r.heading, 45 + Double(i) * 2)
            XCTAssertEqual(r.accelX, 0.5)
            XCTAssertEqual(r.accelY, -1.2 + Double(i) * 0.1)
            XCTAssertEqual(r.accelZ, 9.81)
        }
        // 12 frames a second
        XCTAssertEqual(t.fps, 12, accuracy: 0.5)
    }

    func testProtoEdges() {
        // proto3 defaults, a length-delimited field skipped, an unknown wire type ends the record
        XCTAssertEqual(ProtoReader.decode([]), SeiRecord())
        var r = ProtoReader.decode([0x08, 0x05, 0x12 << 0 | 0, 0x01])
        XCTAssertEqual(r.version, 5)
        r = ProtoReader.decode([0x08, 0x02, 0x8a, 0x01, 0x02, 0xff, 0xff, 0x50, 0x03])
        XCTAssertEqual(r.version, 2)
        XCTAssertEqual(r.autopilot, 3, "field 17 (length 2) skipped, then field 10")
        r = ProtoReader.decode([0x08, 0x02, 0x0b, 0x50, 0x01])
        XCTAssertEqual(r.version, 2)
        XCTAssertEqual(r.autopilot, 0, "wire type 3 stops the record")
        // a short float keeps what came before
        r = ProtoReader.decode([0x08, 0x07, 0x25, 0x00, 0x00])
        XCTAssertEqual(r.version, 7)
        XCTAssertEqual(r.speedMps, 0)
        XCTAssertNil(ProtoReader.decode(base64: "%%%"))
    }

    func testLookups() {
        func s(_ ms: Double, _ v: UInt64) -> TelemetryTrack.Sample { var r = SeiRecord(); r.version = v; return .init(ms: ms, record: r) }
        // decode order is not time order: sorted on load
        let t = TelemetryTrack(samples: [s(200, 3), s(0, 1), s(100, 2)])
        XCTAssertEqual(t.samples.map(\.ms), [0, 100, 200])
        XCTAssertEqual(t.nearest(40)?.version, 1)
        XCTAssertEqual(t.nearest(60)?.version, 2)
        XCTAssertEqual(t.nearest(-50)?.version, 1, "before the first record")
        XCTAssertEqual(t.nearest(3200)?.version, 3, "within 3000 ms of the last")
        XCTAssertNil(t.nearest(3201))
        XCTAssertEqual(t.atOrBefore(99)?.version, 1)
        XCTAssertEqual(t.atOrBefore(100)?.version, 2)
        XCTAssertNil(t.atOrBefore(-1))
        XCTAssertNil(TelemetryTrack(samples: []).nearest(0))
        // a sorted decode keeps equal times in their order
        let a = TelemetryAnswer(v: 1, frames: [TelemetryFrame(tsMs: 5, record: Data([0x08, 0x01]).base64EncodedString()), TelemetryFrame(tsMs: 5, record: Data([0x08, 0x02]).base64EncodedString())])
        XCTAssertEqual(TelemetryTrack(a).samples.map(\.record.version), [1, 2])
    }

    func testDashboardMath() {
        XCTAssertEqual(DashboardMath.speed(13.4112, unit: .mph), 30)
        XCTAssertEqual(DashboardMath.speed(-10, unit: .kmh), 36)
        XCTAssertEqual(DashboardMath.acceleratorPercent(0.42), 42, accuracy: 1e-9)
        XCTAssertEqual(DashboardMath.acceleratorPercent(55), 55)
        XCTAssertEqual(DashboardMath.acceleratorPercent(150), 100)
        XCTAssertFalse(DashboardMath.acceleratorPressed(0.05))
        XCTAssertTrue(DashboardMath.acceleratorPressed(0.06))
        let g = DashboardMath.gForce(ax: 9.81, ay: -29.43)
        XCTAssertEqual(g.gX, 1, accuracy: 1e-9)
        XCTAssertEqual(g.gY, -2, "clamped")
        XCTAssertEqual(g.dotX, 55, accuracy: 1e-9)
        XCTAssertEqual(g.dotY, 80, accuracy: 1e-9)
        XCTAssertTrue(g.braking && g.cornering && g.xHigh && g.yHigh)
        XCTAssertFalse(DashboardMath.gForce(ax: 0, ay: 2.9).accelerating)
        XCTAssertTrue(DashboardMath.gForce(ax: 0, ay: 3).accelerating)
        XCTAssertEqual(DashboardMath.compass(0).point, "N")
        XCTAssertEqual(DashboardMath.compass(22.5).point, "NE")
        XCTAssertEqual(DashboardMath.compass(359).point, "N")
        XCTAssertEqual(DashboardMath.compass(202.4).point, "S")
        XCTAssertEqual(DashboardMath.compass(202.6).degrees, 203)
        XCTAssertTrue(DashboardMath.autopilotHighlighted(1) && DashboardMath.autopilotHighlighted(2))
        XCTAssertFalse(DashboardMath.autopilotHighlighted(3), "TACC is not highlighted")
        var st = DashboardMath.Steering()
        XCTAssertTrue(st.step(target: 90, dt: 1, rate: 1), "dt is capped at 0.1 s")
        XCTAssertEqual(st.shown, 90 * (1 - exp(-4.5)), accuracy: 1e-9)
        for _ in 0..<50 { _ = st.step(target: 90, dt: 0.1, rate: 1) }
        XCTAssertEqual(st.shown, 90, "snaps within 0.05°")
        XCTAssertFalse(st.step(target: 90, dt: 0.1, rate: 1))
    }

    func testMapPath() throws {
        let pts = MapPath.points(try track())
        XCTAssertEqual(pts.count, 24)
        let runs = MapPath.runs(pts)
        XCTAssertEqual(runs.map(\.autopilot), [false, true])
        XCTAssertEqual(runs[0].points.count, 12)
        XCTAssertEqual(runs[1].points.first, runs[0].points.last, "runs overlap by one point")
        XCTAssertFalse(MapPath.valid(lat: 0, lon: 0))
        XCTAssertFalse(MapPath.valid(lat: 91, lon: 0))
        XCTAssertFalse(MapPath.valid(lat: .nan, lon: 1))
        let box = try XCTUnwrap(MapPath.box(pts))
        XCTAssertEqual(box.minLat, 30.2672)
        // the event pin snaps to the route within 500 m
        let near = MapPath.snapEventPin(lat: 30.2673, lon: -97.7430, route: pts)
        XCTAssertTrue(pts.contains { $0.lat == near.lat && $0.lon == near.lon })
        let far = MapPath.snapEventPin(lat: 30.3, lon: -97.7, route: pts)
        XCTAssertEqual(far.lat, 30.3)
        XCTAssertEqual(MapPath.smoothBearing(current: 350, target: 10, factor: 0.5), 0, accuracy: 1e-9)
        XCTAssertEqual(MapPath.smoothBearing(current: 10, target: 350, factor: 0.5), 0, accuracy: 1e-9)
        XCTAssertEqual(MapPath.arrowAnimation(rate: 1), 0.15)
        XCTAssertEqual(MapPath.arrowAnimation(rate: 10), 0.03)
        var r = SeiRecord(); r.latitude = 0.0005; r.longitude = 10
        XCTAssertFalse(MapPath.arrowVisible(r))
        XCTAssertFalse(MapPath.arrowVisible(nil))
    }
}

final class PlaylistTests: XCTestCase {
    func testFromRealRecordings() throws {
        let day = try JSON.decode(Recording.self, from: try apiFixture("recording.json"))
        let ev = try JSON.decode(Recording.self, from: try apiFixture("recording-event.json"))
        let p = PlaylistBuilder.build([day])
        XCTAssertEqual(p.layout, .hw4)
        XCTAssertEqual(p.segments.first?.files.count, 6)
        XCTAssertEqual(p.segments.first?.master, .front)
        XCTAssertEqual(p.segments.map(\.key), p.segments.map(\.key).sorted())
        let e = PlaylistBuilder.build([ev])
        XCTAssertEqual(e.layout, .hw3)
        XCTAssertEqual(e.segments.count, 1)
        XCTAssertEqual(e.segments[0].category, "Sentry")
        // eventAt is the event folder's time (12:30:00) in a clip from 12:29:00: start 5 s before
        XCTAssertEqual(e.segments[0].eventAt, "2025-06-02 12:30:00")
        XCTAssertEqual(PlaylistBuilder.eventStart(segmentKey: e.segments[0].key, eventAt: e.segments[0].eventAt), 55)
        XCTAssertEqual(PlaylistBuilder.eventStart(segmentKey: "2025-06-02_12-30-00", eventAt: "2025-06-02 12:30:02"), 0, "never before the start")
    }

    func testFileNames() {
        XCTAssertEqual(PlaylistBuilder.parse(rel: "TeslaCam/RecentClips/2025-06-01_08-00-00-front.mp4")?.camera, .front)
        XCTAssertEqual(PlaylistBuilder.parse(rel: "x/2019-01-01_01-02-03-left.mp4")?.camera, .leftRepeater, "older firmware")
        XCTAssertEqual(PlaylistBuilder.parse(rel: "x/2019-01-01_01-02-03-RIGHT.MP4")?.camera, .rightRepeater)
        XCTAssertEqual(PlaylistBuilder.parse(rel: "x/2019-01-01_01-02-03-left_pillar.mp4")?.key, "2019-01-01_01-02-03")
        XCTAssertNil(PlaylistBuilder.parse(rel: "x/event.json"))
        XCTAssertNil(PlaylistBuilder.parse(rel: "x/2019-01-01_01-02-03-dashcam.mp4"))
    }

    func testRulesAndMissingCameras() {
        func rec(_ id: String, _ cat: String, _ key: String, _ cams: [String], codec: String? = nil) -> Recording {
            var r = try! JSON.decode(Recording.self, from: json(#"{"id":"\#(id)"}"#))
            r.category = cat
            r.startKey = key
            r.files = cams.map { RecordingFile(rel: "T/\(key)-\($0).mp4", path: "/f/\(key)-\($0).mp4", size: 1, codec: codec) }
            return r
        }
        let list = [rec("b", "Recent", "2025-06-01_09-00-00", ["back"], codec: "h265"), rec("a", "Recent", "2025-06-01_08-00-00", ["front", "back"]), rec("s", "Sentry", "2025-06-01_08-30-00", ["front"])]
        XCTAssertEqual(PlaylistBuilder.select(list, rule: .wholeDay).map(\.id), ["a", "b"])
        XCTAssertEqual(PlaylistBuilder.select(list, rule: .wholeDayEveryCategory).map(\.id), ["a", "s", "b"])
        XCTAssertEqual(PlaylistBuilder.select(list, rule: .recording("s")).map(\.id), ["s"])
        let p = PlaylistBuilder.build(PlaylistBuilder.select(list, rule: .wholeDay))
        XCTAssertEqual(p.segments.count, 2)
        XCTAssertEqual(p.segments[1].master, .back, "no front: the first slot with a file")
        XCTAssertEqual(p.segments[1].codecs[.back], "h265")
        XCTAssertNil(p.segments[0].codecs[.front])
        XCTAssertEqual(p.layout, .hw3)
        XCTAssertEqual(Camera.allCases.filter(\.mirrored), [.back, .leftRepeater, .rightRepeater])
    }

    func testTimeline() {
        var t = Timeline(count: 3)
        XCTAssertEqual(t.starts, [0, 60, 120])
        XCTAssertEqual(t.total, 180)
        t.setDuration(30, at: 1)
        XCTAssertEqual(t.starts, [0, 60, 90])
        t.setDuration(59.5, at: 0)
        XCTAssertEqual(t.starts, [0, 59.5, 89.5])
        XCTAssertEqual(t.locate(70)?.index, 1)
        XCTAssertEqual(t.locate(70)?.local ?? 0, 10.5, accuracy: 1e-9)
        XCTAssertEqual(t.locate(89.5)?.index, 2)
        XCTAssertEqual(t.locate(-5)?.index, 0)
        XCTAssertEqual(t.locate(1000)?.index, 2)
        XCTAssertEqual(t.markerPercent(index: 1, local: 0), 59.5 / 149.5 * 100, accuracy: 1e-9)
        t.setDuration(.nan, at: 0)
        XCTAssertEqual(t.durations[0], 59.5, "a failed probe keeps the value")
        XCTAssertNil(Timeline(count: 0).locate(0))
    }

    func testWallClock() {
        XCTAssertEqual(WallClock.text(segmentKey: "2025-06-01_08-00-00", masterTime: 65.9, clock24: true), "08:01:05")
        XCTAssertEqual(WallClock.text(segmentKey: "2025-06-01_23-59-30", masterTime: 45, clock24: true), "00:00:15", "wraps at 24 h")
        XCTAssertEqual(WallClock.text(segmentKey: "2025-06-01_13-05-00", masterTime: 0, clock24: false), "1:05:00 PM")
        XCTAssertEqual(WallClock.text(segmentKey: "2025-06-01_00-05-00", masterTime: 0, clock24: false), "12:05:00 AM")
        XCTAssertEqual(WallClock.text(segmentKey: "bad", masterTime: 0, clock24: false), "--:--:--")
    }
}

final class SyncTests: XCTestCase {
    func reading(waiting: Bool = false, likely: Bool = true, master: Double = 10, cams: [Camera: SyncPolicy.CameraReading] = [:], rate: Double = 1) -> SyncPolicy.Reading {
        .init(masterWaiting: waiting, masterLikelyToKeepUp: likely, masterTime: master, cameras: cams, rate: rate)
    }

    func testMasterStallAndRestart() {
        var p = SyncPolicy()
        XCTAssertEqual(p.tick(reading(waiting: true)), [], "one tick is not a stall")
        XCTAssertEqual(p.tick(reading(waiting: true)), [.holdOthers])
        XCTAssertEqual(p.tick(reading(likely: false)), [], "held once")
        XCTAssertEqual(p.tick(reading(master: 12.5)), [.restartAll(at: 12.5)])
        XCTAssertEqual(p.tick(reading()), [])
        // a blip that recovers on the next tick is not a stall
        XCTAssertEqual(p.tick(reading(waiting: true)), [])
        XCTAssertEqual(p.tick(reading()), [])
        XCTAssertEqual(p.tick(reading(waiting: true)), [])
    }

    func testDrift() {
        var p = SyncPolicy()
        let cams: [Camera: SyncPolicy.CameraReading] = [
            .back: .init(ready: true, likelyToKeepUp: true, time: 10.05),
            .leftRepeater: .init(ready: true, likelyToKeepUp: true, time: 10.2),
            .rightRepeater: .init(ready: false, likelyToKeepUp: true, time: 3),
        ]
        XCTAssertEqual(p.tick(reading(cams: cams, rate: 2)), [.realign(.leftRepeater, to: 10.2)])
        // a stalled camera is left alone, then realigned when it keeps up
        var stalled = cams
        stalled[.leftRepeater] = .init(ready: true, likelyToKeepUp: false, time: 9)
        XCTAssertEqual(p.tick(reading(cams: stalled)), [])
        stalled[.leftRepeater] = .init(ready: true, likelyToKeepUp: true, time: 10.0)
        XCTAssertEqual(p.tick(reading(cams: stalled)), [.realign(.leftRepeater, to: 10.1)])
    }

    func testPreloadAndRates() {
        XCTAssertTrue(SyncPolicy.shouldPreload(local: 48, duration: 60, rate: 1))
        XCTAssertFalse(SyncPolicy.shouldPreload(local: 47.9, duration: 60, rate: 1))
        XCTAssertTrue(SyncPolicy.shouldPreload(local: 2.5, duration: 5, rate: 1), "a short clip: from half way")
        XCTAssertFalse(SyncPolicy.shouldPreload(local: 1, duration: 5, rate: 1))
        XCTAssertTrue(SyncPolicy.shouldPreload(local: 36, duration: 60, rate: 2))
        XCTAssertEqual(SyncPolicy.effectiveRate(4, canPlayFastForward: false), 2)
        XCTAssertEqual(SyncPolicy.effectiveRate(4, canPlayFastForward: true), 4)
        XCTAssertTrue(SyncPolicy.needsPauseAlign(cameraTime: 10.04, masterTime: 10))
        XCTAssertFalse(SyncPolicy.needsPauseAlign(cameraTime: 10.02, masterTime: 10))
        XCTAssertEqual(SyncPolicy.frameStep(from: 1, by: 1, fps: 36), 1 + 1 / 36.0)
        XCTAssertEqual(SyncPolicy.frameStep(from: 0, by: -1, fps: 30), 0)
    }

    func testSeekCoalescer() {
        var s = SeekCoalescer()
        XCTAssertEqual(s.request(5, dragging: true), .init(time: 5, tolerance: 0.25))
        XCTAssertNil(s.request(6, dragging: true))
        XCTAssertNil(s.request(7, dragging: true))
        XCTAssertEqual(s.completed(), .init(time: 7, tolerance: 0.25), "only the newest waits")
        XCTAssertNil(s.request(8, dragging: false))
        XCTAssertEqual(s.completed(), .init(time: 8, tolerance: 0), "release is exact")
        XCTAssertNil(s.completed())
        XCTAssertFalse(s.busy)
    }
}
