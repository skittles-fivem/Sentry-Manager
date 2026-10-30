import Foundation

/// GET /api/system
public struct SystemAnswer: Codable, Sendable, Equatable {
    public var version: JSONValue?
    public var deployment: String?
    public var converter: ConverterReadiness?
    public var nativeHelper: Bool?
    public var gpu: SystemGPU?
    public var replacementRoots: [ReplacementRoot]?
    public var dataFree: Double?
    public var timezone: String?
    public var security: SystemSecurity?
    public var smbVersions: [String: String]?
    public var smbDefaultVersion: String?
}

public struct ConverterReadiness: Codable, Sendable, Equatable {
    public var ready: Bool?
    public var capabilities: JSONValue?
    public var error: JSONValue?
}

public struct SystemGPU: Codable, Sendable, Equatable {
    public var gpus: [SystemGPUEntry]?
    public var nvidia: [JSONValue]?
    public var hints: [String]?
}

public struct SystemGPUEntry: Codable, Sendable, Equatable {
    public var node: String?
    public var vendor: String?
    public var vendorName: String?
    public var name: String?
    public var cores: Double?
    public var driver: String?
    public var passedThrough: Bool?
    public var encodes: [String: Bool]?
    public var lowPowerOnly: Bool?
}

public struct ReplacementRoot: Codable, Sendable, Equatable {
    public var root: String?
    public var available: Bool?
    public var reason: String?
}

public struct SystemSecurity: Codable, Sendable, Equatable {
    public var https: Bool?
    public var tlsMode: String?
    public var supervised: Bool?
    public var runningAsRoot: Bool?
    public var uid: Double?
    public var account: String?
    public var elevated: Bool?
    public var fullDiskAccess: Bool?
    /// on a Mac: 'daemon' or 'agent', ServiceMode's raw values
    public var runMode: String?

    public var serviceMode: ServiceMode? { runMode.flatMap(ServiceMode.init(rawValue:)) }
}

/// POST /api/studio/browse
public struct BrowseBody: Codable, Sendable {
    public var path: String?
    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(path, forKey: .path)
    }
}

public struct BrowseAnswer: Codable, Sendable, Equatable {
    public var path: String?
    public var parent: String?
    public var entries: [BrowseEntry]?
}

public struct BrowseEntry: Codable, Sendable, Equatable {
    public var name: String?
    public var isDirectory: Bool?
    public var isFile: Bool?
    public var path: String?
}

/// GET /api/studio/context
public struct StudioContext: Codable, Sendable, Equatable {
    public var version: String?
    public var deployment: String?
    public var vehicles: [StudioVehicle]?
    public var outputRoots: [String]?
}

public struct StudioVehicle: Codable, Sendable, Equatable {
    public var id: String?
    public var name: String?
    public var root: String?
    public var timezone: String?
}

/// GET /api/studio/settings: stored keys plus timeFormat
public typealias StudioSettings = [String: JSONValue]

/// GET /api/nodes
public struct NodesAnswer: Codable, Sendable, Equatable {
    public var nodes: [EncoderNode]?
    public var worker: WorkerStatus?
}

public struct EncoderNode: Codable, Sendable, Equatable, Identifiable {
    public var id: String
    public var name: String?
    public var address: String?
    public var pairedAt: Double?
    public var online: Bool?
    public var error: String?
    public var encoders: JSONValue?
    public var slots: Double?
    public var busy: Double?
    public var encoding: JSONValue?
    public var devices: [JSONValue]?
    public var defaultLane: JSONValue?
    public var defaultDeviceName: String?
    public var fingerprint: String?
    public var cars: [String]?
    public var trusted: Bool?
}

public struct WorkerStatus: Codable, Sendable, Equatable {
    public var enabled: Bool?
    public var slots: Double?
    public var use: String?
    public var busy: Double?
    public var pairing: WorkerPairing?
    public var mains: [WorkerMain]?
    public var available: Bool?
    public var name: String?
}

public struct WorkerPairing: Codable, Sendable, Equatable { public var code: String?; public var expiresAt: Double? }

public struct WorkerMain: Codable, Sendable, Equatable {
    public var id: String?
    public var name: String?
    public var address: String?
    public var pairedAt: Double?
    public var lastSeenAt: Double?
}

public struct AddNodeBody: Codable, Sendable { public var address: String; public var code: String }
public struct AddNodeAnswer: Codable, Sendable, Equatable { public var ok: Bool?; public var id: String? }
public struct NodeSlotsBody: Codable, Sendable { public var slots: Double }
public struct NodeUpdateBody: Codable, Sendable {
    public var name: String?
    public var trusted: Bool?
    public var cars: [String]?
    public init(name: String? = nil, trusted: Bool? = nil, cars: [String]? = nil) { self.name = name; self.trusted = trusted; self.cars = cars }
}
public struct NodeUpdateAnswer: Codable, Sendable, Equatable {
    public var ok: Bool?
    public var name: String?
    public var trusted: Bool?
    public var cars: [String]?
}
public struct WorkerEnableBody: Codable, Sendable { public var enabled: Bool }
public struct CodecBody: Codable, Sendable { public var codec: String }

/// GET/POST/DELETE /api/nodes/:id/benchmark
public struct NodeBenchmarkAnswer: Codable, Sendable, Equatable { public var benchmark: NodeBenchmark? }

public struct NodeBenchmark: Codable, Sendable, Equatable {
    public var state: String?
    public var phase: String?
    public var codec: String?
    public var results: [NodeBenchmarkResult]?
    public var suggested: Double?
    public var error: String?
    public var startedAt: Double?
    public var endedAt: Double?
    public var samples: JSONValue?
    public var converted: JSONValue?
}

public struct NodeBenchmarkResult: Codable, Sendable, Equatable {
    public var level: Double?
    public var clipsPerMinute: Double?
    public var clips: Double?
    public var seconds: Double?
    public var failed: Double?
}

/// PUT /api/settings/appearance answers the two values
public typealias AppearanceAnswer = Appearance
