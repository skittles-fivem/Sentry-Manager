import Foundation

/// GET /api/vehicles
public struct VehiclesAnswer: Codable, Sendable, Equatable {
    public var vehicles: [Vehicle]?
}

/// A vehicle as the server sums it up (vehicleSummary in server/index.js).
public struct Vehicle: Codable, Sendable, Equatable, Identifiable {
    public var id: String
    public var name: String?
    public var timezone: String?
    public var source: VehicleSource?
    public var root: String?
    public var studioRoot: String?
    public var share: ShareStatus?
    public var security: String?
    public var inventory: Inventory?
    public var scanning: Bool?
    public var binBytes: Double?
    public var binItems: Double?
    public var policy: Policy?
    public var saved: SavedSummary?
}

/// {type:'smb', share, username, domain, version, encrypt, port, passwordSet}
/// or {type:'path', path}. `password` is only ever sent, never received.
public struct VehicleSource: Codable, Sendable, Equatable {
    public var type: String?
    public var share: String?
    public var username: String?
    public var password: String?
    public var domain: String?
    public var version: String?
    public var encrypt: Bool?
    public var port: Double?
    public var passwordSet: Bool?
    public var path: String?

    public init(type: String?, share: String? = nil, username: String? = nil, password: String? = nil, domain: String? = nil,
                version: String? = nil, encrypt: Bool? = nil, port: Double? = nil, path: String? = nil) {
        self.type = type; self.share = share; self.username = username; self.password = password; self.domain = domain
        self.version = version; self.encrypt = encrypt; self.port = port; self.path = path
    }

    public static func folder(_ path: String) -> VehicleSource { VehicleSource(type: "path", path: path) }
}

public struct ShareStatus: Codable, Sendable, Equatable {
    public var mode: String?
    public var share: String?
    public var path: String?
    public var healthy: Bool?
    public var mounted: Bool?
    public var fsType: String?
    public var source: String?
    public var readable: Bool?
    public var writable: Bool?
    public var renameOverwrite: Bool?
    public var totalBytes: Double?
    public var freeBytes: Double?
    public var lastError: String?
    public var hint: String?
    public var warning: String?
    public var checkedAt: Double?
    public var writeCheckedAt: Double?
    public var mountedAt: Double?
    public var network: Bool?
}

public struct Inventory: Codable, Sendable, Equatable {
    public var recordings: Double?
    public var files: Double?
    public var bytes: Double?
    public var byCategory: [String: CategoryCount]?
    public var scannedAt: Double?
}

public struct CategoryCount: Codable, Sendable, Equatable {
    public var recordings: Double?
    public var bytes: Double?
}

public struct SavedSummary: Codable, Sendable, Equatable {
    public var bytes: Double?
    public var clips: Double?
}

/// POST /api/vehicles and PUT /api/vehicles/:id
public struct VehicleBody: Codable, Sendable, Equatable {
    public var name: String?
    public var timezone: String?
    public var source: VehicleSource?

    public init(name: String? = nil, timezone: String? = nil, source: VehicleSource? = nil) {
        self.name = name; self.timezone = timezone; self.source = source
    }
}

/// POST /api/vehicles/test-smb
public struct TestSMBBody: Codable, Sendable {
    public var vehicleId: String?
    public var source: VehicleSource
    public init(vehicleId: String?, source: VehicleSource) { self.vehicleId = vehicleId; self.source = source }
}

public struct TestSMBAnswer: Codable, Sendable, Equatable {
    public var ok: Bool?
    public var stage: String?
    public var error: String?
    public var hint: String?
    public var ms: Double?
    public var entries: Double?
    public var writable: Bool?
    public var writeProblem: String?
    public var footage: JSONValue?
}

/// POST /api/vehicles/:id/reconnect
public struct ReconnectAnswer: Codable, Sendable, Equatable { public var share: ShareStatus? }

/// POST /api/vehicles/:id/scan
public struct ScanAnswer: Codable, Sendable, Equatable {
    public var ok: Bool?
    public var jobId: Double?
}

/// GET /api/system/volumes (the Mac's drives)
public struct Volume: Codable, Sendable, Equatable {
    public var name: String?
    public var path: String?
    public var fsType: String?
    public var readOnly: Bool?
}
