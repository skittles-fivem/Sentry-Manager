import Foundation
import Observation

/// What differs between the skins' viewers (5.1); the engine is shared.
public struct PlaybackProfile: Sendable, Equatable {
    public enum Lookup: Sendable, Equatable { case nearest, atOrBefore }
    public var cameraOrder: [Camera]
    public var speeds: [Double]
    public var skipSeconds: [Double]
    /// nil = from the telemetry spacing
    public var frameStep: Double?
    public var driftLimit: Double
    public var lookup: Lookup
    public var rule: PlaylistBuilder.Rule
    public var eventLead: Double

    public static let builtin = PlaybackProfile(
        cameraOrder: [.front, .back, .leftRepeater, .rightRepeater, .leftPillar, .rightPillar],
        speeds: [0.5, 1, 1.5, 2, 4], skipSeconds: [5, 15], frameStep: nil, driftLimit: 0.1,
        lookup: .nearest, rule: .wholeDay, eventLead: 5)
}

/// A small least-recently-used cache.
public struct LRUCache<Value: Sendable>: Sendable {
    public let capacity: Int
    private var order: [String] = []
    private var values: [String: Value] = [:]

    public init(capacity: Int) { self.capacity = capacity }

    public mutating func get(_ k: String) -> Value? {
        guard let v = values[k] else { return nil }
        order.removeAll { $0 == k }
        order.append(k)
        return v
    }

    public mutating func put(_ k: String, _ v: Value) {
        if values[k] == nil && values.count >= capacity, let old = order.first {
            order.removeFirst()
            values.removeValue(forKey: old)
        }
        values[k] = v
        order.removeAll { $0 == k }
        order.append(k)
    }

    public var count: Int { values.count }
    public var keys: [String] { order }
}

/// The Viewer: the playlist, the position, telemetry and the dashboard. The
/// app's SyncDriver plays it and reports the master's time back.
@MainActor
@Observable
public final class ViewerModel {
    private let services: Services
    public var profile: PlaybackProfile
    public private(set) var playlist: Playlist?
    public private(set) var timeline = Timeline(count: 0)
    public private(set) var state: LoadState = .idle
    public private(set) var segmentIndex = 0
    /// the master's time inside the segment
    public private(set) var local: Double = 0
    public var rate: Double = 1
    public private(set) var playing = false
    public private(set) var telemetry: TelemetryTrack?
    private var telemetryCache = LRUCache<TelemetryTrack>(capacity: 30)
    private var loadToken = 0
    public var unit: DashboardMath.SpeedUnit = .mph
    public var clock24 = false
    public var sync: SyncPolicy

    public init(services: Services, profile: PlaybackProfile = .builtin) {
        self.services = services
        self.profile = profile
        sync = SyncPolicy(driftLimit: profile.driftLimit)
    }

    public var segment: Segment? { playlist?.segments.indices.contains(segmentIndex) == true ? playlist!.segments[segmentIndex] : nil }
    public var position: Double { timeline.position(index: segmentIndex, local: local) }

    /// a day (per the profile's rule) or one recording
    public func load(vehicleID: String, day: String, recordingID: String? = nil) async {
        state = .loading
        do {
            let list = try await services.api.send(API.recordings(vehicleID, date: day, limit: 500)).recordings ?? []
            let rule: PlaylistBuilder.Rule = recordingID.map { .recording($0) } ?? profile.rule
            let picked = PlaylistBuilder.select(list, rule: rule)
            var details: [Recording] = []
            // up to 6 at a time
            var i = 0
            while i < picked.count {
                let batch = picked[i..<min(i + 6, picked.count)]
                let got = try await withThrowingTaskGroup(of: (Int, Recording).self) { g in
                    for (n, r) in batch.enumerated() { g.addTask { (n, try await self.services.api.send(API.recording(vehicleID, r.id))) } }
                    var out: [(Int, Recording)] = []
                    for try await x in g { out.append(x) }
                    return out.sorted { $0.0 < $1.0 }.map(\.1)
                }
                details += got
                i += 6
            }
            let p = PlaylistBuilder.build(details)
            playlist = p
            timeline = Timeline(count: p.segments.count)
            segmentIndex = 0
            local = 0
            // an event starts a little before the event itself
            if recordingID != nil, let s = p.segments.first, s.eventAt != nil {
                local = PlaylistBuilder.eventStart(segmentKey: s.key, eventAt: s.eventAt, lead: profile.eventLead)
                if local >= Timeline.estimate, let at = locateEvent(p) { segmentIndex = at.0; local = at.1 }
            }
            state = .loaded
            await loadTelemetry()
        } catch {
            state = .failed(errorText(error))
        }
    }

    private func locateEvent(_ p: Playlist) -> (Int, Double)? {
        guard let first = p.segments.first, let ev = first.eventAt, let e = PlaylistBuilder.epochSeconds(ev) else { return nil }
        for (i, s) in p.segments.enumerated() {
            guard let st = PlaylistBuilder.epochSeconds(s.key) else { continue }
            let off = e - profile.eventLead - st
            if off >= 0 && off < timeline.durations[i] { return (i, off) }
        }
        return nil
    }

    /// a master item loaded, or a background probe finished
    public func durationKnown(_ seconds: Double, segment: Int) { timeline.setDuration(seconds, at: segment) }

    /// the player's time, from the periodic observer
    public func playerTime(_ t: Double) { local = t }

    public func setPlaying(_ on: Bool) { playing = on }

    /// moves to a playlist position; answers whether the segment changed
    @discardableResult
    public func seek(to t: Double) async -> Bool {
        guard let at = timeline.locate(t) else { return false }
        let changed = at.index != segmentIndex
        segmentIndex = at.index
        local = at.local
        if changed { await loadTelemetry() }
        return changed
    }

    /// the master's item ended: the next segment, or the end
    public func segmentEnded() async -> Bool {
        guard let p = playlist, segmentIndex + 1 < p.segments.count else { playing = false; return false }
        segmentIndex += 1
        local = 0
        await loadTelemetry()
        return true
    }

    public var shouldPreload: Bool {
        guard timeline.durations.indices.contains(segmentIndex) else { return false }
        return SyncPolicy.shouldPreload(local: local, duration: timeline.durations[segmentIndex], rate: rate)
    }

    /// the master clip's telemetry, cached; a newer load wins
    public func loadTelemetry() async {
        guard let s = segment, let cam = s.master, let path = s.files[cam] else { telemetry = nil; return }
        if let t = telemetryCache.get(path) { telemetry = t; return }
        loadToken += 1
        let token = loadToken
        telemetry = nil
        guard let a = try? await services.api.send(API.telemetry(path: path)) else { return }
        let track = TelemetryTrack(a)
        telemetryCache.put(path, track)
        if token == loadToken { telemetry = track }
    }

    /// the record shown now, by the profile's rule
    public var record: SeiRecord? {
        let ms = local * 1000
        switch profile.lookup {
        case .nearest: return telemetry?.nearest(ms)
        case .atOrBefore: return telemetry?.atOrBefore(ms)
        }
    }

    public var speedText: String? { record.map { String(DashboardMath.speed(Double($0.speedMps), unit: unit)) } }
    public var wallClock: String { segment.map { WallClock.text(segmentKey: $0.key, masterTime: local, clock24: clock24) } ?? "--:--:--" }

    /// the frame step: pause, then ±1 frame from the master's time
    public func stepTarget(_ count: Int) -> Double {
        let fps = profile.frameStep.map { 1 / $0 } ?? telemetry?.fps ?? 36
        return SyncPolicy.frameStep(from: local, by: count, fps: fps)
    }

    /// cameras in the profile's order with their files, mirrored or not
    public var tiles: [(camera: Camera, path: String?, mirrored: Bool)] {
        let cams = playlist?.layout == .hw4 ? profile.cameraOrder : profile.cameraOrder.filter { $0 != .leftPillar && $0 != .rightPillar }
        return cams.map { ($0, segment?.files[$0], $0.mirrored) }
    }

    public var routePoints: [MapPath.Point] { telemetry.map(MapPath.points) ?? [] }
}
