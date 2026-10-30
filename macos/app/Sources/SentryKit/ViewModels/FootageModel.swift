import Foundation
import Observation

/// The Footage page: days in pages of 30, the days the reader opened, the
/// selection, protect and Move to trash, and the speed index (6.3).
@MainActor
@Observable
public final class FootageModel {
    public static let pageSize = 30
    private let services: Services
    public var vehicleID: String?
    /// 'all', 'Recent', 'Sentry', 'Saved', 'Other'
    public var category = "all"
    public var date = ""
    public private(set) var offset = 0
    public private(set) var data: DaysAnswer?
    public private(set) var state: LoadState = .idle
    /// days the reader opened (none opens by itself) and their recordings
    public private(set) var open = Set<String>()
    public private(set) var dayRecordings: [String: [Recording]] = [:]
    public var selected = Set<String>()
    public private(set) var speedStatus: SpeedsStatus?
    private let speedPoller: Poller

    public init(services: Services) {
        self.services = services
        speedPoller = Poller(clock: services.clock)
    }

    public var days: [FootageDay] { data?.days ?? [] }
    public var total: Int { Int(data?.total ?? 0) }
    public var hasNewer: Bool { offset > 0 }
    public var hasOlder: Bool { offset + Self.pageSize < total }

    public func load() async {
        guard let id = vehicleID else { data = nil; state = .loaded; return }
        state = .loading
        do {
            data = try await services.api.send(API.days(id, category: category, offset: offset, limit: Self.pageSize, date: date))
            state = .loaded
            for d in open { await loadDay(d) }
        } catch {
            state = .failed(errorText(error))
        }
    }

    public func setFilter(category: String? = nil, date: String? = nil) async {
        if let category { self.category = category }
        if let date { self.date = date }
        offset = 0
        selected = []
        await load()
    }

    public func older() async { guard hasOlder else { return }; offset += Self.pageSize; await load() }
    public func newer() async { guard hasNewer else { return }; offset = max(0, offset - Self.pageSize); await load() }

    public func toggle(day: String) async {
        if open.contains(day) { open.remove(day) } else { open.insert(day); await loadDay(day) }
    }

    public func loadDay(_ day: String) async {
        guard let id = vehicleID else { return }
        if let a = try? await services.api.send(API.recordings(id, category: category, date: day, limit: 500)) {
            dayRecordings[day] = a.recordings ?? []
        }
    }

    public var selectedRecordings: [Recording] { dayRecordings.values.flatMap { $0 }.filter { selected.contains($0.id) } }

    public func protect(_ on: Bool) async throws -> Int {
        guard let id = vehicleID, !selected.isEmpty else { return 0 }
        let a = try await services.api.send(API.protect(id, ids: Array(selected), protected: on))
        await load()
        return Int(a.count ?? 0)
    }

    /// the confirm the web shows before Move to trash
    public func trashConfirm() -> ConfirmText {
        let recs = selectedRecordings
        return Confirms.trashRecordings(count: selected.count, bytes: recs.reduce(0) { $0 + ($1.bytes ?? 0) }, anyProtected: recs.contains { $0.protected == true })
    }

    public func trash() async throws -> TrashAnswer {
        guard let id = vehicleID else { return TrashAnswer() }
        let a = try await services.api.send(API.trash(id, ids: Array(selected)))
        selected = []
        await load()
        return a
    }

    /// Delete this day / through this date: the preview, then the confirm or a toast
    public enum DayDelete: Sendable, Equatable { case nothing(String), confirm(ConfirmText) }

    public func previewDayDelete(_ day: String, through: Bool) async throws -> DayDelete {
        guard let id = vehicleID else { return .nothing("Nothing to move.") }
        let p = try await services.api.send(API.trashDays(id, TrashDaysBody(day: day, through: through, category: category, preview: true)))
        if let toast = Confirms.trashDaysNothing(p) { return .nothing(toast) }
        return .confirm(Confirms.trashDays(day: day, through: through, category: category, preview: p))
    }

    public func deleteDay(_ day: String, through: Bool) async throws -> TrashDaysAnswer {
        guard let id = vehicleID else { return TrashDaysAnswer() }
        let a = try await services.api.send(API.trashDays(id, TrashDaysBody(day: day, through: through, category: category)))
        await load()
        return a
    }

    /// "Index speeds": start the index, or follow one that runs, every 3 s
    public func indexSpeeds(start: Bool) async {
        if start { speedStatus = try? await services.api.send(API.startSpeedIndex()) }
        speedPoller.start(every: 3) { [weak self] in
            guard let self else { return }
            self.speedStatus = try? await self.services.api.send(API.speedsStatus())
            if self.speedStatus?.running != true { self.speedPoller.stop(); await self.load() }
        }
    }

    public func disappear() { speedPoller.stop() }

    public func thumbnailPath(_ rec: Recording) -> String? {
        guard let id = vehicleID else { return nil }
        return API.thumbnailPath(id, rec.id)
    }
}
