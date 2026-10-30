import Foundation

// Policies, the Recycle Bin, jobs, notifications, settings, updates,
// interface packs, logs and security.

public struct Policy: Codable, Sendable, Equatable {
    public var enabled: Bool?
    public var runAt: String?
    /// days per category, null = keep forever
    public var keepDays: [String: Double?]?
    public var lastRunAt: Double?
    public var lastResult: JSONValue?
}

public struct PolicyPreview: Codable, Sendable, Equatable {
    public var recordings: Double?
    public var bytes: Double?
    public var byCategory: [String: CategoryCount]?
    public var ids: [String]?
}

/// GET/PUT /api/vehicles/:id/policy
public struct PolicyAnswer: Codable, Sendable, Equatable {
    public var policy: Policy?
    public var preview: PolicyPreview?
}

public struct PolicyBody: Codable, Sendable, Equatable {
    public var enabled: Bool?
    public var runAt: String?
    public var keepDays: [String: Double?]?
    public init(enabled: Bool? = nil, runAt: String? = nil, keepDays: [String: Double?]? = nil) {
        self.enabled = enabled; self.runAt = runAt; self.keepDays = keepDays
    }

    // keepDays must send its nulls ("keep forever"), not leave them out
    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encodeIfPresent(enabled, forKey: .enabled)
        try c.encodeIfPresent(runAt, forKey: .runAt)
        if let keepDays {
            var k = c.nestedContainer(keyedBy: AnyKey.self, forKey: .keepDays)
            for (cat, days) in keepDays {
                if let d = days { try k.encode(d, forKey: AnyKey(cat)) } else { try k.encodeNil(forKey: AnyKey(cat)) }
            }
        }
    }
}

struct AnyKey: CodingKey {
    var stringValue: String
    var intValue: Int? { nil }
    init(_ s: String) { stringValue = s }
    init?(stringValue: String) { self.stringValue = stringValue }
    init?(intValue: Int) { nil }
}

/// GET /api/bin
public struct BinAnswer: Codable, Sendable, Equatable { public var items: [BinItem]? }

public struct BinItem: Codable, Sendable, Equatable, Identifiable {
    public var id: String
    public var vehicleId: String?
    public var label: String?
    public var category: String?
    public var startAt: String?
    public var bytes: Double?
    public var files: [BinFile]?
    public var trashedAt: Double?
    public var reason: String?
}

public struct BinFile: Codable, Sendable, Equatable {
    public var rel: String?
    public var binRel: String?
    public var size: Double?
}

public struct BinRestoreAnswer: Codable, Sendable, Equatable { public var restored: Double?; public var conflicts: Double? }
public struct BinDeleteAnswer: Codable, Sendable, Equatable { public var bytes: Double? }
public struct BinEmptyAnswer: Codable, Sendable, Equatable { public var deleted: Double?; public var failed: Double?; public var bytes: Double? }
public struct ConfirmFlagBody: Codable, Sendable { public var confirm: Bool = true; public var vehicle: String? }

/// GET /api/jobs
public struct JobsAnswer: Codable, Sendable, Equatable { public var jobs: [Job]? }

public struct Job: Codable, Sendable, Equatable, Identifiable {
    public var id: Double
    public var type: String?
    public var vehicleId: String?
    /// 'running', 'done', 'failed', 'cancelled'
    public var status: String?
    public var title: String?
    public var detail: String?
    public var startedAt: Double?
    public var finishedAt: Double?
    public var result: JSONValue?
    public var externalId: String?
    public var energy: JobEnergy?

    public var isRunning: Bool { status == "running" }
    public var isConversion: Bool { type == "conversion" }
}

public struct JobEnergy: Codable, Sendable, Equatable {
    public var wh: Double?
    public var avgW: Double?
    public var cost: Double?
}

/// POST /api/jobs/clear
public struct JobsClearBody: Codable, Sendable {
    public var vehicle: String?
    public var preview: Bool?
    public var confirm: Bool?
    public init(vehicle: String?, preview: Bool? = nil, confirm: Bool? = nil) { self.vehicle = vehicle; self.preview = preview; self.confirm = confirm }

    // vehicle:null means every vehicle and must be sent
    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(vehicle, forKey: .vehicle)
        try c.encodeIfPresent(preview, forKey: .preview)
        try c.encodeIfPresent(confirm, forKey: .confirm)
    }
}
public struct JobsClearAnswer: Codable, Sendable, Equatable { public var jobs: Double?; public var cleared: Double? }

/// GET /api/notifications
public struct NotificationsAnswer: Codable, Sendable, Equatable { public var notifications: [AppNotification]? }

public struct AppNotification: Codable, Sendable, Equatable, Identifiable {
    public var id: Double
    public var kind: String?
    /// 'info', 'warning', 'critical'
    public var level: String?
    public var vehicleId: String?
    public var title: String?
    public var message: String?
    public var createdAt: Double?
    public var updatedAt: Double?
    public var resolvedAt: Double?
    public var readAt: Double?

    public var isOpen: Bool { resolvedAt == nil }
    public var isUnread: Bool { readAt == nil }
}

public struct NotificationIDsBody: Codable, Sendable { public var ids: [Double] }

/// GET /api/settings
public struct SettingsAnswer: Codable, Sendable, Equatable {
    public var appearance: Appearance?
    public var power: PowerSettings?
    /// only where same-computer sign-in exists (2.6)
    public var localSignIn: LocalSignInSetting?
}

public struct Appearance: Codable, Sendable, Equatable {
    public var reduceEffects: Bool?
    public var clock24: Bool?
    public init(reduceEffects: Bool? = nil, clock24: Bool? = nil) { self.reduceEffects = reduceEffects; self.clock24 = clock24 }
}

public struct PowerSettings: Codable, Sendable, Equatable {
    public var currency: String?
    /// 'flat' or a time-of-use mode
    public var mode: String?
    public var pricePerKwh: Double?
    public var defaultPrice: Double?
    public var rates: [PowerRate]?
}

public struct PowerRate: Codable, Sendable, Equatable {
    public var from: String?
    public var to: String?
    public var price: Double?
    public var days: [Double]?
    public var label: String?
}

public struct LocalSignInSetting: Codable, Sendable, Equatable {
    public var enabled: Bool?
    public var user: String?
    public var via: String?
}

public struct LocalSignInSettingBody: Codable, Sendable {
    public var enabled: Bool
    /// nil goes out as null: the oldest account (2.6), not "unchanged"
    public var user: String?

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(enabled, forKey: .enabled)
        try c.encode(user, forKey: .user)
    }
}

/// GET /api/updates
public struct UpdatesStatus: Codable, Sendable, Equatable {
    public var enabled: Bool?
    public var image: String?
    public var tag: String?
    public var current: UpdateBuild?
    public var latest: JSONValue?
    public var checkedAt: Double?
    public var error: String?
    public var updateAvailable: Bool?
    public var critical: CriticalUpdate?
    public var how: String?
}

public struct UpdateBuild: Codable, Sendable, Equatable {
    public var sha: String?
    public var builtAt: String?
}

public struct CriticalUpdate: Codable, Sendable, Equatable {
    public var level: String?
    public var reason: String?
    public var created: String?
}

/// GET /api/ui-packs
public struct UIPacksAnswer: Codable, Sendable, Equatable {
    public var active: String?
    public var packs: [UIPack]?
    public var original: Bool?
}

public struct UIPack: Codable, Sendable, Equatable, Identifiable {
    public var id: String
    public var name: String?
    public var description: String?
    public var author: String?
    public var files: Double?
    public var bytes: Double?
    public var replaces: [String]?
    public var uploadedAt: Double?
    public var builtin: Bool?
}

public struct UIPackActiveBody: Codable, Sendable {
    public var id: String?
    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(id, forKey: .id)
    }
}
public struct UIPackActiveAnswer: Codable, Sendable, Equatable { public var ok: Bool?; public var active: String? }
public struct UIPackUploadAnswer: Codable, Sendable, Equatable { public var ok: Bool?; public var pack: UIPack? }

/// GET /api/logs
public struct LogsAnswer: Codable, Sendable, Equatable { public var lines: [LogLine]? }

public struct LogLine: Codable, Sendable, Equatable {
    public var at: Double?
    public var level: String?
    public var scope: String?
    public var message: String?
}

public struct LogArchivesAnswer: Codable, Sendable, Equatable { public var archives: [LogArchive]? }
public struct LogArchive: Codable, Sendable, Equatable { public var name: String?; public var bytes: Double?; public var at: Double? }
public struct LogsClearAnswer: Codable, Sendable, Equatable { public var ok: Bool?; public var archived: String? }

/// GET /api/security
public struct SecurityAnswer: Codable, Sendable, Equatable {
    public var https: Bool?
    public var tlsMode: String?
    public var trustProxy: Bool?
    public var tls: TLSInfo?
    public var supervised: Bool?
    public var runningAsRoot: Bool?
    public var currentHost: String?
    public var confirmed: Bool?
    public var account: String?
    public var elevated: Bool?
    public var fullDiskAccess: Bool?
}

public struct TLSInfo: Codable, Sendable, Equatable {
    public var mode: String?
    public var ca: CertificateInfo?
    public var certificate: CertificateInfo?
    public var hostnames: [String]?
    public var editable: [String]?
    public var fromEnv: [String]?
    public var containerHostname: String?
    public var error: String?
}

public struct CertificateInfo: Codable, Sendable, Equatable {
    public var fingerprint: String?
    public var validFrom: String?
    public var validTo: String?
    public var expiresAt: Double?
    public var subject: String?
    public var issuer: String?
    public var names: [String]?
}

public struct HostnamesBody: Codable, Sendable { public var hostnames: [String] }
public struct HostnamesAnswer: Codable, Sendable, Equatable {
    public var ok: Bool?
    public var caReplaced: Bool?
    public var tls: TLSInfo?
}
