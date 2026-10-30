import Foundation

/// /api/converter/invoke/* always answers 200 with this envelope.
public struct ConverterEnvelope<Value: Decodable & Sendable>: Decodable, Sendable {
    public var ok: Bool
    public var value: Value?
    public var error: ConverterError?

    enum CodingKeys: String, CodingKey { case ok, value, error }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        ok = (try? c.decode(Bool.self, forKey: .ok)) ?? false
        error = try? c.decodeIfPresent(ConverterError.self, forKey: .error)
        value = ok ? try c.decodeIfPresent(Value.self, forKey: .value) : nil
    }
}

public struct ConverterError: Codable, Sendable, Equatable, Error {
    public var code: String?
    public var retryable: Bool?
    public var details: JSONValue?
    public init(code: String?, retryable: Bool? = nil, details: JSONValue? = nil) { self.code = code; self.retryable = retryable; self.details = details }
}

/// What an invoke turned into.
public enum ConverterResult<Value: Sendable>: Sendable {
    case value(Value?)
    case failure(ConverterError)
}

extension ConverterResult: Equatable where Value: Equatable {}

public struct ConverterCapabilities: Codable, Sendable, Equatable {
    public var separatePublication: String?
    public var ownershipCleanup: String?
    /// 'available' or 'unavailable'
    public var replacement: String?
    public var destinationContainment: String?
    public var privateTempCleanup: String?
    public var splitSave: Bool?
}

/// One conversion job as the converter shows it (getCurrent, progress events).
public struct ConverterSnapshot: Codable, Sendable, Equatable {
    public var schemaVersion: Double?
    public var id: String?
    /// 'discovering', 'waiting', 'running', 'paused', 'completed', 'failed', 'cancelled', ...
    public var state: String?
    public var revision: Double?
    public var config: ConverterConfig?
    public var counters: ConverterCounters?
    public var summary: ConverterSummary?
    public var summaryDismissed: Bool?
    public var discoveryComplete: Bool?
    public var createdAt: Double?
    public var updatedAt: Double?
    public var pausedMs: Double?
    public var items: [ConverterItem]?
    public var activeItemId: String?
    public var timings: JSONValue?
    public var recovery: JSONValue?
    public var progress: JSONValue?
    public var itemCount: Double?
    public var itemStateCounts: [String: Double]?
    public var cleanupPendingCount: Double?
    public var itemsTruncated: Bool?
    public var fps: JSONValue?
    public var waitingOnNode: JSONValue?
    public var encodingNow: JSONValue?

    public var isFinished: Bool { ["completed", "failed", "cancelled"].contains(state ?? "") }

    public init() {}
}

public struct ConverterConfig: Codable, Sendable, Equatable {
    public var sourceRoot: String?
    public var destinationRoot: String?
    public var codec: String?
    public var profileId: String?
    public var networkMode: String?
    public var outputMode: String?
    public var replacementConfirmed: Bool?
    public var includedGroups: [String]?
    public var groupOrder: [String]?
    public var verifyMode: String?
    public var concurrency: Double?
    public var lanes: JSONValue?
    public var bitrateKbps: Double?
    public var device: String?

    public init(sourceRoot: String? = nil, destinationRoot: String? = nil, codec: String? = nil, profileId: String? = nil,
                networkMode: String? = nil, outputMode: String? = nil, replacementConfirmed: Bool? = nil,
                includedGroups: [String]? = nil, verifyMode: String? = nil) {
        self.sourceRoot = sourceRoot; self.destinationRoot = destinationRoot; self.codec = codec; self.profileId = profileId
        self.networkMode = networkMode; self.outputMode = outputMode; self.replacementConfirmed = replacementConfirmed
        self.includedGroups = includedGroups; self.verifyMode = verifyMode
    }
}

public struct ConverterGroupCounters: Codable, Sendable, Equatable {
    public var files: Double?
    public var sourceBytes: Double?
    public var outputBytes: Double?
    public var savedBytes: Double?
}

public struct ConverterCounters: Codable, Sendable, Equatable {
    public var files: Double?
    public var sourceVideoBytes: Double?
    public var outputVideoBytes: Double?
    public var savedBytes: Double?
    public var copiedAssetBytes: Double?
    public var groups: [String: ConverterGroupCounters]?

    public init(sourceVideoBytes: Double? = nil) { self.sourceVideoBytes = sourceVideoBytes }
}

public struct ConverterSummary: Codable, Sendable, Equatable {
    public var files: Double?
    public var sourceVideoBytes: Double?
    public var outputVideoBytes: Double?
    public var savedBytes: Double?
    public var copiedAssetBytes: Double?
    public var groups: [String: ConverterGroupCounters]?
    public var source: String?
    public var output: String?
    public var saved: String?
    public var copiedAssets: String?
}

public struct ConverterItem: Codable, Sendable, Equatable, Identifiable {
    public var id: String
    public var state: String?
    public var kind: String?
    public var group: String?
    public var eventId: String?
    public var relativePath: String?
    public var sourcePath: String?
    public var sourceBytes: Double?
    public var mtimeMs: Double?
    public var timestampKey: String?
    public var camera: String?
    public var eligibleVideo: Bool?
    public var attempts: Double?
    public var resultCode: String?
    public var failure: ConverterError?
    public var assetType: String?
    public var offTarget: OffTarget?
}

public struct OffTarget: Codable, Sendable, Equatable {
    public var savedPercent: Double?
    public var targetPercent: Double?
}

/// event: converter:progress
public struct ConverterProgressEvent: Codable, Sendable, Equatable {
    public var jobId: String?
    public var revision: Double?
    public var snapshot: ConverterSnapshot?
    /// the snapshot has only id, revision and progress
    public var progressOnly: Bool

    enum CodingKeys: String, CodingKey { case jobId, revision, snapshot }

    public init(jobId: String?, revision: Double?, snapshot: ConverterSnapshot?, progressOnly: Bool) {
        self.jobId = jobId; self.revision = revision; self.snapshot = snapshot; self.progressOnly = progressOnly
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        jobId = try? c.decodeIfPresent(String.self, forKey: .jobId)
        revision = try? c.decodeIfPresent(Double.self, forKey: .revision)
        snapshot = try? c.decodeIfPresent(ConverterSnapshot.self, forKey: .snapshot)
        let raw = ((try? c.decodeIfPresent([String: JSONValue].self, forKey: .snapshot)) ?? nil) ?? [:]
        progressOnly = Set(raw.keys).isSubset(of: ["id", "revision", "progress"])
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encodeIfPresent(jobId, forKey: .jobId)
        try c.encodeIfPresent(revision, forKey: .revision)
        try c.encodeIfPresent(snapshot, forKey: .snapshot)
    }
}

public struct ConverterStartAnswer: Codable, Sendable, Equatable { public var jobId: String? }

/// invoke devices
public struct ConverterDevices: Codable, Sendable, Equatable {
    public var devices: [ConverterDevice]?
    public var defaultDevice: String?
    public var defaultDevices: JSONValue?
}

public struct ConverterDevice: Codable, Sendable, Equatable, Identifiable {
    public var id: String
    public var kind: String?
    public var vendor: String?
    public var name: String?
    public var node: String?
    public var index: Double?
    public var pci: String?
    public var deviceId: String?
    public var nodeName: String?
    public var deviceName: String?
    public var online: Bool?
    public var isDefault: Bool?
    public var slots: Double?
    public var encoders: [String: JSONValue]?
    // a Mac's own GPU only (darwinDevices.js): the chip, its GPU cores, its
    // video encode engines and whether it runs in a virtual machine
    public var chip: String?
    public var gpuCores: Double?
    public var encodeEngines: Double?
    public var virtual: Bool?
}

/// invoke getAuto
public struct ConverterAuto: Codable, Sendable, Equatable {
    public var settings: AutoSettings?
    public var status: AutoStatus?
    public var saved: [JSONValue]?
}

public struct AutoSettings: Codable, Sendable, Equatable {
    public var enabled: Bool?
    public var intervalMinutes: Double?
    public var window: JSONValue?
    public var holdManual: Bool?
    public var keepRunning: Bool?
    public var openAtLogin: Bool?
    public var configs: [JSONValue]?
}

public struct AutoStatus: Codable, Sendable, Equatable {
    public var lastCheckAt: Double?
    public var nextCheckAt: Double?
    public var lastNewFiles: Double?
    public var lastJobId: String?
    public var activeJobId: String?
    public var pausedForWindow: Bool?
    public var checking: Bool?
    public var error: String?
    public var activeSource: String?
}

/// subscribeProgress, unsubscribeProgress, pause, ...: value is unimportant
public struct ConverterAck: Codable, Sendable, Equatable {}
