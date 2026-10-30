import Foundation
import XCTest
@testable import SentryKit

/// The expected texts come from running web/app.js's own functions
/// (Tests/fixtures-gen/format.js), not from typing them in.
final class FormatTests: XCTestCase {
    var table: [String: JSONValue] = [:]

    override func setUpWithError() throws {
        table = try JSON.decode([String: JSONValue].self, from: try fixture("format-expected.json"))
    }

    func rows(_ name: String) -> [[JSONValue]] { (table[name]?.array ?? []).compactMap(\.array) }

    func check(_ name: String, _ f: (JSONValue) -> String, file: StaticString = #filePath, line: UInt = #line) {
        let r = rows(name)
        XCTAssertFalse(r.isEmpty, name, file: file, line: line)
        for row in r {
            XCTAssertEqual(f(row[0]), row[1].string, "\(name)(\(row[0]))", file: file, line: line)
        }
    }

    func testNumberBasics() {
        check("toFixed") { a in JS.toFixed(a.array![0].number!, Int(a.array![1].number!)) }
        check("localeString") { JS.localeString($0.number!) }
    }

    func testSizes() {
        check("bytes") { Format.bytes($0.number) }
        check("size") { Format.size($0.number) }
        check("rate") { Format.rate($0.number) }
        check("sizeOf") { a in Format.sizeOf(a.array![0].number!, a.array![1].number!, sep: a.array![2].string!) }
    }

    func testNumbers() {
        check("pct") { Format.pct($0.number!) }
        check("watts") { Format.watts($0.number!) }
        check("temp") { Format.temp($0.number!) }
        check("clock") { Format.clock($0.number!) }
        check("energyText") { Format.energyText($0.number!) }
        check("money") { a in Format.money(a.array![0].number!, a.array![1].string) }
        check("linkSpeed") { Format.linkSpeed($0.number) }
        for row in rows("clampPct") { XCTAssertEqual(Format.clampPct(row[0].number), row[1].number, "clampPct(\(row[0]))") }
        check("plural") { a in Format.plural(a.array![0].number, a.array![1].string!) }
    }

    func testTimes() {
        check("took") { Format.took($0.number!) }
        check("span") { Format.span($0.number!) }
        check("answer") { Format.answer($0.number!) }
        check("clockText12") { Format.clockText($0.string, clock24: false) }
        check("clockText24") { Format.clockText($0.string, clock24: true) }
    }

    func testNames() {
        check("humanize") { Format.humanize($0.string) }
        check("cpuName") { Format.cpuName($0.string) }
        for row in rows("sensorName") {
            let s = row[0]
            XCTAssertEqual(Format.sensorName(chip: s["chip"]?.string, label: s["label"]?.string, kind: s["kind"]?.string), row[1].string, "\(s)")
            XCTAssertEqual(Format.sensorName(chip: s["chip"]?.string, label: s["label"]?.string, kind: s["kind"]?.string, multiSocket: true), row[2].string, "\(s)")
        }
    }

    func testAgoAndWhen() {
        let utc = TimeZone(identifier: "UTC")!
        let now = 1_750_000_000_000.0
        XCTAssertEqual(Format.ago(nil, now: now), "never")
        XCTAssertEqual(Format.ago(now - 30_000, now: now), "just now")
        XCTAssertEqual(Format.ago(now - 5 * 60_000, now: now), "5 min ago")
        XCTAssertEqual(Format.ago(now - 3 * 3_600_000, now: now), "3 h ago")
        XCTAssertEqual(Format.ago(now - 2 * 86_400_000, now: now, timeZone: utc), "6/13/2025")
        XCTAssertEqual(Format.when(1_748_764_800_000, clock24: false, timeZone: utc), "6/1/2025, 8:00:00 AM")
        XCTAssertEqual(Format.when(1_748_764_800_000, clock24: true, timeZone: utc), "6/1/2025, 08:00:00")
        XCTAssertEqual(Format.when(nil, clock24: true), "—")
        XCTAssertEqual(Format.timeHMS(0, clock24: false), "12:00:00 AM")
        XCTAssertEqual(Format.timeHMS(43200, clock24: false), "12:00:00 PM")
        XCTAssertEqual(Confirms.dayTitle("2025-06-01"), "Sunday, Jun 1, 2025")
    }

    func testSensorHot() {
        XCTAssertTrue(Format.sensorHot(chip: "drivetemp", kind: "temp", value: 55, name: "x"))
        XCTAssertFalse(Format.sensorHot(chip: "nvme", kind: "temp", value: 69, name: "NVMe"))
        XCTAssertTrue(Format.sensorHot(chip: "coretemp", kind: "temp", value: 90, name: "CPU package"))
        XCTAssertFalse(Format.sensorHot(chip: "x", kind: "fan", value: 4000, name: "Fan 1"))
    }
}

final class InterfaceTests: XCTestCase {
    func packs(active: String?, builtin: Bool = true, name: String = "X") throws -> UIPacksAnswer {
        var a = try JSON.decode(UIPacksAnswer.self, from: try apiFixture("ui-packs.json"))
        a.active = active
        if let active, !(a.packs ?? []).contains(where: { $0.id == active }) {
            a.packs?.append(try JSON.decode(UIPack.self, from: json(#"{"id":"\#(active)","name":"\#(name)","builtin":\#(builtin)}"#)))
        }
        if let active { a.packs = a.packs?.map { var p = $0; if p.id == active { p.builtin = builtin }; return p } }
        return a
    }

    func testResolver() throws {
        XCTAssertEqual(InterfaceResolver.resolve(try packs(active: nil)), .skin(.builtin))
        for id in ["horizon", "flux", "aurora", "daylight"] {
            XCTAssertEqual(InterfaceResolver.resolve(try packs(active: id)), .skin(SkinID(rawValue: id)!))
            // the same id without builtin is not ours
            if case .thirdParty = InterfaceResolver.resolve(try packs(active: id, builtin: false)) {} else { XCTFail(id) }
        }
        XCTAssertEqual(InterfaceResolver.resolve(try packs(active: "my-pack", builtin: false, name: "Horizon")), .thirdParty(id: "my-pack", name: "Horizon"))
        XCTAssertEqual(InterfaceResolver.resolve(try packs(active: "horizon"), override: true), .skin(.builtin))
        XCTAssertEqual(InterfaceResolver.resolve(nil, lastSkin: .aurora), .skin(.aurora))
        XCTAssertEqual(InterfaceResolver.resolve(nil), .skin(.builtin))
    }

    func testPageMapping() {
        for skin in SkinID.allCases {
            let pages = PageList.pages(skin)
            for p in [PageID.home, .footage, .viewer, .encoder, .nodes, .policies, .jobs, .bin, .notifications, .settings] {
                XCTAssertTrue(pages.contains { $0.page == p }, "\(skin) has \(p)")
            }
        }
        XCTAssertEqual(PageList.host(.serverLogs, in: .builtin), .home)
        XCTAssertEqual(PageList.host(.serverLogs, in: .daylight), .serverLogs)
        XCTAssertEqual(PageList.host(.vehicleEncoding, in: .builtin), .encoder)
        XCTAssertEqual(PageList.label(.home, in: .aurora), "Sky")
        XCTAssertEqual(PageList.label(.nodes, in: .daylight), "Helper servers")
        XCTAssertEqual(PageList.label(.notifications, in: .horizon), "Notifications")
        XCTAssertEqual(PageList.windowTitle(.footage, in: .builtin), "Footage · Sentry Six Manager")
        XCTAssertEqual(PageList.windowTitle(.home, in: .horizon), "Dashboard · Horizon · Sentry Six Manager")
        XCTAssertEqual(PageList.windowTitle(.home, in: .daylight, openAlerts: 2), "(2) Today · Sentry Six")
        XCTAssertEqual(PageList.pages(.builtin).map(\.label), ["Overview", "Nodes", "Footage", "Viewer", "Encoder", "Policies", "Jobs", "Recycle Bin", "Notifications", "Settings"])
    }

    func testRoutes() {
        XCTAssertEqual(Route.parse("viewer?rec=abc&day=2025-06-01&autoplay=0"), Route(.viewer, ["rec": "abc", "day": "2025-06-01", "autoplay": "0"]))
        XCTAssertEqual(Route.parse("settings/vehicles?add=1"), Route(.settings, ["tab": "vehicles", "add": "1"]))
        XCTAssertEqual(Route.parse("vehicles?edit=model-y"), Route(.settings, ["tab": "vehicles", "edit": "model-y"]))
        XCTAssertEqual(Route.parse("jobs?clear=1"), Route(.jobs, ["clear": "1"]))
        XCTAssertEqual(Route.parse("footage?day=2025-06-01%20x")?.params["day"], "2025-06-01 x")
        XCTAssertNil(Route.parse("nowhere"))
    }

    func testSkinPrefs() {
        let store = MemoryPrefs()
        SkinPrefs(skin: .horizon, store: store).set("sky", "dusk")
        SkinPrefs(skin: .daylight, store: store).set("theme", "dark")
        SkinPrefs(skin: .flux, store: store).setBool("collapsed", true)
        SkinPrefs(skin: .aurora, store: store).set("glass", "1")
        XCTAssertEqual(store.all, ["horizon.sky": "dusk", "daylight.theme": "dark", "flux.collapsed": "true", "aurora.glass": "1"])
        XCTAssertTrue(SkinPrefs(skin: .flux, store: store).bool("collapsed", default: false))
        XCTAssertEqual(SkinPrefs(skin: .builtin, store: store).key("collapsed"), "sm.collapsed")
    }

    func testOddDigitsDoNotCrash() {
        // only ASCII digits count, as in the web's JavaScript
        XCTAssertEqual(Format.clockText("2026-10-29 \u{0661}\u{0665}:10:02", clock24: false), "2026-10-29 \u{0661}\u{0665}:10:02 AM")
        XCTAssertEqual(Format.sensorName(chip: "coretemp", label: "Package id 99999999999999999999", kind: nil, multiSocket: true),
                       "CPU package 99999999999999999999")
    }
}
