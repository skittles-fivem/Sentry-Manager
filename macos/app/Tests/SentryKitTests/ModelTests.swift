import Foundation
import XCTest
@testable import SentryKit

/// Every dumped server answer decodes into its model.
final class ModelTests: XCTestCase {
    func decode<T: Decodable>(_ type: T.Type, _ name: String, file: StaticString = #filePath, line: UInt = #line) throws -> T {
        do { return try JSON.decode(type, from: try apiFixture(name)) } catch let e as XCTSkip { throw e } catch {
            XCTFail("\(name): \(error)", file: file, line: line)
            throw error
        }
    }

    func testEveryFixtureDecodes() throws {
        let map: [(String, (Data) throws -> Any)] = [
            ("auth-me.json", { try JSON.decode(AuthMe.self, from: $0) }),
            ("auth-me-needs-setup.json", { try JSON.decode(AuthMe.self, from: $0) }),
            ("auth-me-signed-out.json", { try JSON.decode(AuthMe.self, from: $0) }),
            ("info.json", { try JSON.decode(ServerInfo.self, from: $0) }),
            ("setup-ok.json", { try JSON.decode(OKAnswer.self, from: $0) }),
            ("confirm-ok.json", { try JSON.decode(OKAnswer.self, from: $0) }),
            ("logout.json", { try JSON.decode(OKAnswer.self, from: $0) }),
            ("vehicles.json", { try JSON.decode(VehiclesAnswer.self, from: $0) }),
            ("vehicle-add.json", { try JSON.decode(Vehicle.self, from: $0) }),
            ("days.json", { try JSON.decode(DaysAnswer.self, from: $0) }),
            ("days-sentry.json", { try JSON.decode(DaysAnswer.self, from: $0) }),
            ("recordings.json", { try JSON.decode(RecordingsAnswer.self, from: $0) }),
            ("recordings-all.json", { try JSON.decode(RecordingsAnswer.self, from: $0) }),
            ("recording.json", { try JSON.decode(Recording.self, from: $0) }),
            ("recording-event.json", { try JSON.decode(Recording.self, from: $0) }),
            ("telemetry.json", { try JSON.decode(TelemetryAnswer.self, from: $0) }),
            ("speeds.json", { try JSON.decode(DaySpeedsAnswer.self, from: $0) }),
            ("telemetry-speeds.json", { try JSON.decode(ClipSpeedsAnswer.self, from: $0) }),
            ("speeds-status.json", { try JSON.decode(SpeedsStatus.self, from: $0) }),
            ("policy.json", { try JSON.decode(PolicyAnswer.self, from: $0) }),
            ("policy-put.json", { try JSON.decode(PolicyAnswer.self, from: $0) }),
            ("protect.json", { try JSON.decode(ProtectAnswer.self, from: $0) }),
            ("trash.json", { try JSON.decode(TrashAnswer.self, from: $0) }),
            ("bin.json", { try JSON.decode(BinAnswer.self, from: $0) }),
            ("bin-restore.json", { try JSON.decode(BinRestoreAnswer.self, from: $0) }),
            ("settings.json", { try JSON.decode(SettingsAnswer.self, from: $0) }),
            ("settings-appearance.json", { try JSON.decode(Appearance.self, from: $0) }),
            ("system.json", { try JSON.decode(SystemAnswer.self, from: $0) }),
            ("system-volumes.json", { try JSON.decode([Volume].self, from: $0) }),
            ("security.json", { try JSON.decode(SecurityAnswer.self, from: $0) }),
            ("logs.json", { try JSON.decode(LogsAnswer.self, from: $0) }),
            ("logs-archive.json", { try JSON.decode(LogArchivesAnswer.self, from: $0) }),
            ("ui-packs.json", { try JSON.decode(UIPacksAnswer.self, from: $0) }),
            ("nodes.json", { try JSON.decode(NodesAnswer.self, from: $0) }),
            ("stats.json", { try JSON.decode(Stats.self, from: $0) }),
            ("stats-after.json", { try JSON.decode(Stats.self, from: $0) }),
            ("stats-history.json", { try JSON.decode(StatsHistory.self, from: $0) }),
            ("notifications.json", { try JSON.decode(NotificationsAnswer.self, from: $0) }),
            ("notifications-open.json", { try JSON.decode(NotificationsAnswer.self, from: $0) }),
            ("updates.json", { try JSON.decode(UpdatesStatus.self, from: $0) }),
            ("studio-settings.json", { try JSON.decode(StudioSettings.self, from: $0) }),
            ("studio-context.json", { try JSON.decode(StudioContext.self, from: $0) }),
            ("studio-browse.json", { try JSON.decode(BrowseAnswer.self, from: $0) }),
            ("studio-browse-root.json", { try JSON.decode(BrowseAnswer.self, from: $0) }),
            ("jobs.json", { try JSON.decode(JobsAnswer.self, from: $0) }),
            ("converter-getCapabilities.json", { try JSON.decode(ConverterEnvelope<ConverterCapabilities>.self, from: $0) }),
            ("converter-getCurrent-null.json", { try JSON.decode(ConverterEnvelope<ConverterSnapshot>.self, from: $0) }),
            ("converter-getCurrent-running.json", { try JSON.decode(ConverterEnvelope<ConverterSnapshot>.self, from: $0) }),
            ("converter-getCurrent-done.json", { try JSON.decode(ConverterEnvelope<ConverterSnapshot>.self, from: $0) }),
            ("converter-devices.json", { try JSON.decode(ConverterEnvelope<ConverterDevices>.self, from: $0) }),
            ("converter-getAuto.json", { try JSON.decode(ConverterEnvelope<ConverterAuto>.self, from: $0) }),
            ("converter-start.json", { try JSON.decode(ConverterEnvelope<ConverterStartAnswer>.self, from: $0) }),
            ("converter-subscribeProgress.json", { try JSON.decode(ConverterEnvelope<JSONValue>.self, from: $0) }),
            ("converter-unknown.json", { try JSON.decode(ConverterEnvelope<JSONValue>.self, from: $0) }),
            ("converter-start-refused.json", { try JSON.decode(ConverterEnvelope<ConverterStartAnswer>.self, from: $0) }),
        ]
        for (name, decode) in map {
            XCTAssertNoThrow(try decode(try apiFixture(name)), name)
        }
    }

    func testEveryJSONFixtureIsCovered() throws {
        let dir = Bundle.module.url(forResource: "api", withExtension: nil, subdirectory: "Fixtures")!
        let names = try FileManager.default.contentsOfDirectory(atPath: dir.path).filter { $0.hasSuffix(".json") }
        XCTAssertGreaterThan(names.count, 60)
        // the error answers all decode as the envelope
        for n in names where n.contains("error") || n.contains("401") || n.contains("403") || n.contains("400") {
            let wrapped = try JSON.decode([String: JSONValue].self, from: try apiFixture(n))
            if let body = wrapped["body"], case .object = body {
                let env = try JSON.decode(ErrorEnvelope.self, from: try JSON.encode(body))
                XCTAssertNotNil(env.error, n)
            }
        }
    }

    func testRealValues() throws {
        let me = try decode(AuthMe.self, "auth-me.json")
        XCTAssertEqual(me.user, "admin")
        XCTAssertEqual(me.deployment, "macos")
        XCTAssertEqual(me.secure, true)
        XCTAssertNotNil(me.fingerprint)
        XCTAssertEqual(try decode(AuthMe.self, "auth-me-needs-setup.json").needsSetup, true)

        let v = try XCTUnwrap(try decode(VehiclesAnswer.self, "vehicles.json").vehicles?.first)
        XCTAssertEqual(v.id, "model-y")
        XCTAssertEqual(v.source?.type, "path")
        XCTAssertEqual(v.policy?.keepDays?["Recent"], .some(nil))

        let rec = try decode(Recording.self, "recording-event.json")
        XCTAssertEqual(rec.category, "Sentry")
        XCTAssertEqual(rec.event?.city, "Austin")
        XCTAssertEqual(rec.event?.est_lat?.value ?? 0, 30.2672, accuracy: 1e-9)

        let done = try decode(ConverterEnvelope<ConverterSnapshot>.self, "converter-getCurrent-done.json")
        XCTAssertTrue(done.ok)
        XCTAssertEqual(done.value?.state, "completed")
        XCTAssertGreaterThan(done.value?.items?.count ?? 0, 0)
        XCTAssertEqual(try decode(ConverterEnvelope<JSONValue>.self, "converter-unknown.json").error?.code, "UNKNOWN_CHANNEL")

        let packs = try decode(UIPacksAnswer.self, "ui-packs.json")
        XCTAssertEqual(Set(packs.packs?.filter { $0.builtin == true }.map(\.id) ?? []), ["horizon", "flux", "aurora", "daylight"])
        XCTAssertNil(packs.active)

        let n = try XCTUnwrap(try decode(NotificationsAnswer.self, "notifications-open.json").notifications?.first)
        XCTAssertEqual(n.kind, "conversion")
        XCTAssertTrue(n.isOpen && n.isUnread)

        let jobs = try decode(JobsAnswer.self, "jobs.json").jobs ?? []
        XCTAssertTrue(jobs.contains { $0.isConversion })
    }

    func testLenientDecoding() throws {
        // unknown keys, fractions where a whole number usually is, nulls
        let v = try JSON.decode(Vehicle.self, from: json(#"{"id":"a","name":null,"binItems":2.5,"futureKey":{"x":1},"share":null}"#))
        XCTAssertEqual(v.binItems, 2.5)
        XCTAssertNil(v.name)
        let day = try JSON.decode(FootageDay.self, from: json(#"{"day":"2025-06-01","speedIndex":null,"codecs":["h265"]}"#))
        XCTAssertEqual(day.codecs, ["h265"])
        // est_lat as a number or a string
        XCTAssertEqual(try JSON.decode(TeslaEvent.self, from: json(#"{"est_lat":"1.5","est_lon":2}"#)).est_lon?.value, 2)
    }

    func testRequestBodiesSendTheirNulls() throws {
        let p = String(decoding: try JSON.encode(PolicyBody(keepDays: ["Recent": 30, "Saved": nil])), as: UTF8.self)
        XCTAssertTrue(p.contains(#""Saved":null"#), p)
        XCTAssertEqual(String(decoding: try JSON.encode(BrowseBody(path: nil)), as: UTF8.self), #"{"path":null}"#)
        XCTAssertEqual(String(decoding: try JSON.encode(UIPackActiveBody(id: nil)), as: UTF8.self), #"{"id":null}"#)
        XCTAssertEqual(String(decoding: try JSON.encode(JobsClearBody(vehicle: nil, preview: true)), as: UTF8.self), #"{"preview":true,"vehicle":null}"#)
    }
}
