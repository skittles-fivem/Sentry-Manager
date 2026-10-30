import Foundation

/// GET /api/vehicles/:id/days
public struct DaysAnswer: Codable, Sendable, Equatable {
    public var total: Double?
    public var recordings: Double?
    public var offset: Double?
    public var limit: Double?
    public var scannedAt: Double?
    public var scanning: Bool?
    public var error: String?
    public var dates: [String]?
    public var days: [FootageDay]?
}

public struct FootageDay: Codable, Sendable, Equatable, Identifiable {
    public var day: String
    public var recordings: Double?
    public var byCategory: [String: Double]?
    public var bytes: Double?
    public var minutes: Double?
    public var clips: Double?
    public var encoded: Double?
    public var encodedRecordings: Double?
    public var unknown: Double?
    public var codecs: [String]?
    public var cameras: Double?
    public var `protected`: Double?
    public var thumbId: String?
    public var speedIndex: SpeedIndexCount?
    public var id: String { day }
}

public struct SpeedIndexCount: Codable, Sendable, Equatable {
    public var clips: Double?
    public var indexed: Double?
}

/// GET /api/vehicles/:id/recordings
public struct RecordingsAnswer: Codable, Sendable, Equatable {
    public var total: Double?
    public var offset: Double?
    public var limit: Double?
    public var scannedAt: Double?
    public var scanning: Bool?
    public var error: String?
    public var dates: [String]?
    public var recordings: [Recording]?
}

/// A recording; GET .../recordings/:rid adds root, folderPath, files, event.
public struct Recording: Codable, Sendable, Equatable, Identifiable {
    public var id: String
    public var key: String?
    public var vehicleId: String?
    public var type: String?
    public var category: String?
    public var folder: String?
    public var eventId: String?
    public var startKey: String?
    public var start: String?
    public var end: String?
    public var eventAt: String?
    public var day: String?
    public var fileCount: Double?
    public var clipCount: Double?
    public var minutes: Double?
    public var bytes: Double?
    public var cameras: [String]?
    public var thumbRel: String?
    public var frontRel: String?
    public var `protected`: Bool?
    public var mtimeMs: Double?
    public var encoded: EncodedCount?
    // detail only
    public var root: String?
    public var folderPath: String?
    public var files: [RecordingFile]?
    public var event: TeslaEvent?
}

public struct EncodedCount: Codable, Sendable, Equatable {
    public var clips: Double?
    public var encoded: Double?
    public var unknown: Double?
    public var codecs: [String]?
}

public struct RecordingFile: Codable, Sendable, Equatable {
    public var rel: String
    public var path: String?
    public var size: Double?
    /// 'h264', 'h265', 'av1' or null (not known yet)
    public var codec: String?
}

/// A Tesla event.json; the lat/lon come as strings.
public struct TeslaEvent: Codable, Sendable, Equatable {
    public var timestamp: String?
    public var city: String?
    public var street: String?
    public var est_lat: LooseNumber?
    public var est_lon: LooseNumber?
    public var reason: String?
    public var camera: String?
}

/// POST /api/vehicles/:id/protect
public struct ProtectBody: Codable, Sendable { public var ids: [String]; public var `protected`: Bool }
public struct ProtectAnswer: Codable, Sendable, Equatable { public var ok: Bool?; public var count: Double? }

/// POST /api/vehicles/:id/trash
public struct IDsBody: Codable, Sendable { public var ids: [String] }
public struct TrashAnswer: Codable, Sendable, Equatable {
    public var moved: [String]?
    public var skipped: [TrashSkip]?
    public var bytes: Double?
    public var deferred: Bool?
}
public struct TrashSkip: Codable, Sendable, Equatable {
    public var id: String?
    public var reason: String?
    public var message: String?
}

/// POST /api/vehicles/:id/trash-days
public struct TrashDaysBody: Codable, Sendable {
    public var day: String
    public var through: Bool?
    public var category: String?
    public var preview: Bool?
    public init(day: String, through: Bool? = nil, category: String? = nil, preview: Bool? = nil) {
        self.day = day; self.through = through; self.category = category; self.preview = preview
    }
}
public struct TrashDaysAnswer: Codable, Sendable, Equatable {
    public var recordings: Double?
    public var bytes: Double?
    public var `protected`: Double?
    public var days: Double?
    public var oldest: String?
    public var started: Bool?
}

/// GET /api/telemetry
public struct TelemetryAnswer: Codable, Sendable, Equatable {
    public var v: Double?
    /// [tsMs, base64 record]
    public var frames: [TelemetryFrame]?
}

public struct TelemetryFrame: Codable, Sendable, Equatable {
    public var tsMs: Double
    public var record: String

    public init(tsMs: Double, record: String) { self.tsMs = tsMs; self.record = record }

    public init(from decoder: Decoder) throws {
        var c = try decoder.unkeyedContainer()
        tsMs = (try? c.decode(Double.self)) ?? 0
        record = (try? c.decode(String.self)) ?? ""
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.unkeyedContainer()
        try c.encode(tsMs)
        try c.encode(record)
    }
}

/// GET /api/vehicles/:id/speeds: each clip's speeds per second, or null
public struct DaySpeedsAnswer: Codable, Sendable, Equatable {
    public var speeds: [String: [Double?]?]?
}

/// POST /api/telemetry/speeds
public struct SpeedsBody: Codable, Sendable { public var paths: [String] }
public struct ClipSpeedsAnswer: Codable, Sendable, Equatable { public var speeds: [[Double?]?]? }

/// GET /api/speeds/status, POST /api/speeds/index
public struct SpeedsStatus: Codable, Sendable, Equatable {
    public var running: Bool?
    public var done: Double?
    public var total: Double?
    public var day: String?
    public var finishedAt: Double?
    public var last: Double?
}
