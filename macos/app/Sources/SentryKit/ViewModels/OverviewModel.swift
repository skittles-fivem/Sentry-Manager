import Foundation
import Observation

/// A fixed-size series of samples (oldest dropped), for sparklines.
public struct RingBuffer: Sendable, Equatable {
    public let keep: Int
    public private(set) var values: [Double?] = []

    public init(keep: Int) { self.keep = keep }

    public mutating func append(_ v: Double?) {
        values.append(v)
        if values.count > keep { values.removeFirst(values.count - keep) }
    }

    public mutating func fill(_ list: [Double?]) {
        values = Array(list.suffix(keep))
    }
}

/// The server numbers (Overview, the Encoder's hardware panel): /api/stats
/// every 2 s while showing, and the history for 15 min or 24 h.
@MainActor
@Observable
public final class StatsModel {
    public static let seriesNames = ["cpu", "memory", "gpuBusy", "gpuVideo", "powerW", "rxBps", "txBps", "tempMax"]
    private let services: Services
    public private(set) var stats: Stats?
    public private(set) var history: StatsHistory?
    public private(set) var historyAt: Double = 0
    public private(set) var series: [String: RingBuffer]
    public private(set) var error: String?
    /// '15m' or '24h'
    public var range: String {
        didSet { services.prefs.set("sm.statsRange", range) }
    }
    public let keep: Int
    private let poller: Poller

    public init(services: Services, keep: Int = 180) {
        self.services = services
        self.keep = keep
        range = services.prefs.value("sm.statsRange") == "24h" ? "24h" : "15m"
        series = Dictionary(uniqueKeysWithValues: Self.seriesNames.map { ($0, RingBuffer(keep: keep)) })
        poller = Poller(clock: services.clock)
    }

    /// prefill the rings from the last 15 minutes
    public func prefill() async {
        guard let h = try? await services.api.send(API.statsHistory(range: "15m")) else { return }
        for name in Self.seriesNames { series[name]?.fill(h.series?[name] ?? []) }
    }

    public func refresh() async {
        do {
            let s = try await services.api.send(API.stats())
            stats = s
            error = nil
            series["cpu"]?.append(s.cpu?.usage)
            series["memory"]?.append(s.memory.flatMap { m in m.usedB.flatMap { u in m.totalB.map { $0 > 0 ? u / $0 * 100 : nil } } } ?? nil)
            series["gpuBusy"]?.append(s.gpus?.compactMap(\.busy).max())
            series["gpuVideo"]?.append(s.gpus?.compactMap(\.video).max())
            series["powerW"]?.append(s.power?.totalW)
            let ifs = s.network?.interfaces ?? []
            series["rxBps"]?.append(ifs.isEmpty ? nil : ifs.compactMap(\.rxBps).reduce(0, +))
            series["txBps"]?.append(ifs.isEmpty ? nil : ifs.compactMap(\.txBps).reduce(0, +))
            series["tempMax"]?.append((s.sensors ?? []).filter { $0.kind == "temp" }.compactMap(\.value).max())
        } catch {
            self.error = errorText(error)
        }
        // the 24 h history is read at most once a minute
        if range == "24h" && services.nowMs() - historyAt >= 60_000 {
            if let h = try? await services.api.send(API.statsHistory(range: "24h")) { history = h; historyAt = services.nowMs() }
        }
    }

    public func show(every seconds: Double = 2) { poller.start(every: seconds) { [weak self] in await self?.refresh() } }
    public func hide() { poller.stop() }

    public func csvPath() -> String { API.statsCSVPath(range: range) }
}

/// The log lines (Overview › Logs, and the packs' log pages).
@MainActor
@Observable
public final class LogsModel {
    private let services: Services
    public private(set) var lines: [LogLine] = []
    public private(set) var archives: [LogArchive] = []
    public var limit = 300
    /// nil = warnings and errors (the server's default), "all" = everything
    public var level: String?
    public var scope: String?
    public var text = ""
    public private(set) var error: String?
    private let poller: Poller

    public init(services: Services) {
        self.services = services
        poller = Poller(clock: services.clock)
    }

    public func load() async {
        do {
            lines = try await services.api.send(API.logs(limit: limit, level: level)).lines ?? []
            error = nil
        } catch { self.error = errorText(error) }
    }

    public func loadArchives() async {
        archives = (try? await services.api.send(API.logArchives()).archives) ?? archives
    }

    /// the converter preset: converter, nodes and stats lines, 400 of them
    public func converterPreset() { limit = 400; level = "all"; scope = "converter|nodes|stats" }

    public var shown: [LogLine] {
        let re = scope.flatMap { try? NSRegularExpression(pattern: $0) }
        let needle = text.lowercased()
        return lines.filter { l in
            if let re, let s = l.scope, re.firstMatch(in: s, range: NSRange(s.startIndex..., in: s)) == nil { return false }
            if !needle.isEmpty && !(l.message ?? "").lowercased().contains(needle) && !(l.scope ?? "").lowercased().contains(needle) { return false }
            return true
        }
    }

    public func clear() async throws {
        _ = try await services.api.send(API.clearLogs())
        await load()
        await loadArchives()
    }

    /// the packs follow the log every 5 s; the built-in skin does not
    public func follow(every seconds: Double = 5) { poller.start(every: seconds) { [weak self] in await self?.load() } }
    public func stopFollowing() { poller.stop() }
}

/// The Overview page: its tiles come from the shell, StatsModel and LogsModel.
@MainActor
@Observable
public final class OverviewModel {
    public let stats: StatsModel
    public let logs: LogsModel
    public private(set) var security: SecurityAnswer?

    public init(services: Services, keep: Int = 180) {
        stats = StatsModel(services: services, keep: keep)
        logs = LogsModel(services: services)
        self.services = services
    }

    private let services: Services

    public func appear() async {
        await stats.prefill()
        stats.show()
        async let l: Void = logs.load()
        async let s: SecurityAnswer? = try? services.api.send(API.security())
        security = await s
        _ = await l
    }

    public func disappear() { stats.hide(); logs.stopFollowing() }
}
