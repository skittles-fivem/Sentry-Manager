import Foundation

/// The six camera slots, in the order the players are made.
public enum Camera: String, Sendable, CaseIterable, Codable {
    case front, back
    case leftRepeater = "left_repeater"
    case rightRepeater = "right_repeater"
    case leftPillar = "left_pillar"
    case rightPillar = "right_pillar"

    /// back and the repeaters are drawn mirrored, as in the car
    public var mirrored: Bool { self == .back || self == .leftRepeater || self == .rightRepeater }

    /// lower case; older firmware says left/right for the repeaters
    public static func normalize(_ name: String) -> Camera? {
        let n = name.lowercased()
        if n == "left" { return .leftRepeater }
        if n == "right" { return .rightRepeater }
        return Camera(rawValue: n)
    }
}

public enum CameraLayout: String, Sendable, Equatable { case hw3, hw4 }

/// Every camera file that shares one time key: one minute of footage.
public struct Segment: Sendable, Equatable {
    public var key: String
    public var files: [Camera: String]
    public var codecs: [Camera: String]
    public var recordingId: String
    public var category: String?
    public var eventAt: String?

    /// the master camera: front, else the first slot with a file
    public var master: Camera? {
        if files[.front] != nil { return .front }
        return Camera.allCases.first { files[$0] != nil }
    }
}

public struct Playlist: Sendable, Equatable {
    public var segments: [Segment]
    public var layout: CameraLayout
}

/// Builds the Viewer's playlist from recordings and their files (7.1).
public enum PlaylistBuilder {
    public enum Rule: Sendable, Equatable {
        /// the built-in "whole day": Recent only
        case wholeDay
        /// the packs' "whole day, every category"
        case wholeDayEveryCategory
        /// one recording (an event, or "Play recording")
        case recording(String)
    }

    static let fileName = try! NSRegularExpression(pattern: "(\\d{4}-\\d{2}-\\d{2}_\\d{2}-\\d{2}-\\d{2})-([a-z0-9_]+)\\.mp4$", options: [.caseInsensitive])

    /// (key, camera) from a file's rel path
    public static func parse(rel: String) -> (key: String, camera: Camera)? {
        let r = NSRange(rel.startIndex..., in: rel)
        guard let m = fileName.firstMatch(in: rel, range: r),
              let k = Range(m.range(at: 1), in: rel), let c = Range(m.range(at: 2), in: rel),
              let cam = Camera.normalize(String(rel[c])) else { return nil }
        return (String(rel[k]), cam)
    }

    /// which recordings a rule keeps, sorted by startKey
    public static func select(_ recordings: [Recording], rule: Rule) -> [Recording] {
        let kept: [Recording]
        switch rule {
        case .wholeDay: kept = recordings.filter { $0.category == "Recent" }
        case .wholeDayEveryCategory: kept = recordings
        case .recording(let id): kept = recordings.filter { $0.id == id }
        }
        return kept.sorted { ($0.startKey ?? "") < ($1.startKey ?? "") }
    }

    /// `details` are the recordings with their files (GET .../recordings/:rid)
    public static func build(_ details: [Recording]) -> Playlist {
        var segments: [Segment] = []
        for rec in details.sorted(by: { ($0.startKey ?? "") < ($1.startKey ?? "") }) {
            var byKey: [String: Segment] = [:]
            for f in rec.files ?? [] {
                guard let (key, cam) = parse(rel: f.rel), let path = f.path else { continue }
                var s = byKey[key] ?? Segment(key: key, files: [:], codecs: [:], recordingId: rec.id, category: rec.category, eventAt: rec.eventAt)
                s.files[cam] = path
                if let c = f.codec { s.codecs[cam] = c }
                byKey[key] = s
            }
            segments += byKey.values.sorted { $0.key < $1.key }
        }
        let hw4 = segments.contains { $0.files[.leftPillar] != nil || $0.files[.rightPillar] != nil }
        return Playlist(segments: segments, layout: hw4 ? .hw4 : .hw3)
    }

    /// "2025-06-01_08-00-00" as seconds since midnight, and the day
    public static func clockSeconds(_ key: String) -> Int? {
        let parts = key.split(separator: "_")
        guard parts.count == 2 else { return nil }
        let t = parts[1].split(separator: "-").compactMap { Int($0) }
        guard t.count == 3 else { return nil }
        return t[0] * 3600 + t[1] * 60 + t[2]
    }

    /// seconds of a time key or of an event's "YYYY-MM-DD HH:MM:SS" / ISO time
    public static func epochSeconds(_ s: String) -> Double? {
        let digits = s.split { !$0.isNumber }.compactMap { Int($0) }
        guard digits.count >= 6 else { return nil }
        var c = DateComponents()
        c.year = digits[0]; c.month = digits[1]; c.day = digits[2]; c.hour = digits[3]; c.minute = digits[4]; c.second = digits[5]
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        return cal.date(from: c)?.timeIntervalSince1970
    }

    /// An event playlist starts 5 s before the event (a product choice).
    public static func eventStart(segmentKey: String, eventAt: String?, lead: Double = 5) -> Double {
        guard let eventAt, let e = epochSeconds(eventAt), let s = epochSeconds(segmentKey) else { return 0 }
        return max(0, e - lead - s)
    }
}

/// Segment durations and where each starts in the playlist (7.1 step 4).
public struct Timeline: Sendable, Equatable {
    public static let estimate: Double = 60
    public private(set) var durations: [Double]
    public private(set) var starts: [Double]

    public init(count: Int) {
        durations = Array(repeating: Self.estimate, count: count)
        starts = []
        recompute(from: 0)
    }

    public var total: Double { (starts.last ?? 0) + (durations.last ?? 0) }

    /// a real duration replaces the estimate; later starts move
    public mutating func setDuration(_ seconds: Double, at index: Int) {
        guard durations.indices.contains(index), seconds.isFinite, seconds > 0 else { return }
        durations[index] = seconds
        recompute(from: index)
    }

    private mutating func recompute(from index: Int) {
        if starts.count != durations.count { starts = Array(repeating: 0, count: durations.count) }
        var t = index > 0 ? starts[index - 1] + durations[index - 1] : 0
        for i in index..<durations.count { starts[i] = t; t += durations[i] }
    }

    /// the segment and the time inside it for a playlist position
    public func locate(_ t: Double) -> (index: Int, local: Double)? {
        guard !durations.isEmpty else { return nil }
        let clamped = max(0, min(t, total))
        var lo = 0, hi = starts.count - 1
        while lo < hi {
            let mid = (lo + hi + 1) / 2
            if starts[mid] <= clamped { lo = mid } else { hi = mid - 1 }
        }
        return (lo, min(clamped - starts[lo], durations[lo]))
    }

    public func position(index: Int, local: Double) -> Double { (starts.indices.contains(index) ? starts[index] : 0) + local }

    /// where an event marker sits on the bar, 0...100
    public func markerPercent(index: Int, local: Double) -> Double {
        total > 0 ? position(index: index, local: local) / total * 100 : 0
    }
}

/// The time shown over the video: the first file's key plus the master's
/// whole seconds, wrapped at 24 h.
public enum WallClock {
    public static func seconds(segmentKey: String, masterTime: Double) -> Int? {
        guard let base = PlaylistBuilder.clockSeconds(segmentKey) else { return nil }
        return (base + Int(floor(max(0, masterTime)))) % 86400
    }

    public static func text(segmentKey: String, masterTime: Double, clock24: Bool) -> String {
        guard let s = seconds(segmentKey: segmentKey, masterTime: masterTime) else { return "--:--:--" }
        return Format.timeHMS(s, clock24: clock24)
    }
}
