import Foundation

/// GET /api/stats
public struct Stats: Codable, Sendable, Equatable {
    public var at: Double?
    public var intervalMs: Double?
    public var host: StatsHost?
    public var cpu: StatsCPU?
    public var memory: StatsMemory?
    public var gpus: [StatsGPU]?
    public var power: StatsPower?
    public var disks: [StatsDisk]?
    public var filesystems: [StatsFilesystem]?
    public var network: StatsNetwork?
    public var sensors: [StatsSensor]?
    public var processes: StatsProcesses?
    public var alerts: [StatsAlert]?
    public var unlock: [JSONValue]?
    public var sparks: [String: [Double?]]?
    public var price: StatsPrice?
    public var encoder: StatsEncoder?
}

public struct StatsHost: Codable, Sendable, Equatable {
    public var hostname: String?
    public var os: String?
    public var kernel: String?
    public var arch: String?
    public var version: String?
    public var node: String?
    public var uptimeS: Double?
    public var appUptimeS: Double?
    public var ffmpeg: String?
}

public struct StatsCPU: Codable, Sendable, Equatable {
    public var model: String?
    public var cores: Double?
    public var threads: Double?
    public var usage: Double?
    public var perCore: [Double?]?
    public var freqMHz: Double?
    public var tempC: Double?
    public var load: [Double]?
    public var app: StatsAppCPU?
    public var throttling: Bool?
    public var sockets: Double?
    public var perSocket: [JSONValue]?
}

public struct StatsAppCPU: Codable, Sendable, Equatable {
    public var usage: Double?
    public var ffmpegUsage: Double?
    public var ffmpegCount: Double?
}

public struct StatsMemory: Codable, Sendable, Equatable {
    public var totalB: Double?
    public var usedB: Double?
    public var availableB: Double?
    public var cacheB: Double?
    public var swapTotalB: Double?
    public var swapUsedB: Double?
    public var appB: Double?
}

public struct StatsGPU: Codable, Sendable, Equatable {
    public var id: String?
    public var name: String?
    public var vendor: String?
    public var driver: String?
    public var node: String?
    public var busy: Double?
    public var video: Double?
    public var vramUsedB: Double?
    public var vramTotalB: Double?
    public var clockMHz: Double?
    public var tempC: Double?
    public var powerW: Double?
    public var clients: Double?
    public var integrated: Bool?
}

public struct StatsPower: Codable, Sendable, Equatable {
    public var totalW: Double?
    public var cpuW: Double?
    public var gpuW: Double?
    public var chipW: Double?
    public var discreteW: Double?
    public var cpuSource: String?
    public var gpuSource: String?
    public var cpuUnavailable: String?
    public var todayWh: Double?
    public var weekWh: Double?
    public var todayCost: Double?
    public var weekCost: Double?
    public var priceNow: Double?
    public var nodes: [StatsNodePower]?
    public var combined: StatsCombinedPower?
}

public struct StatsNodePower: Codable, Sendable, Equatable {
    public var name: String?
    public var online: Bool?
    public var totalW: Double?
    public var cpuW: Double?
    public var gpuW: Double?
    public var cpuUnavailable: String?
}

public struct StatsCombinedPower: Codable, Sendable, Equatable {
    public var totalW: Double?
    public var reporting: Double?
    public var servers: Double?
    public var todayWh: Double?
    public var todayCost: Double?
    public var weekWh: Double?
    public var weekCost: Double?
}

public struct StatsDisk: Codable, Sendable, Equatable {
    public var name: String?
    public var model: String?
    public var kind: String?
    public var readBps: Double?
    public var writeBps: Double?
    public var tempC: Double?
}

public struct StatsFilesystem: Codable, Sendable, Equatable {
    public var label: String?
    public var path: String?
    public var vehicle: Bool?
    public var totalB: Double?
    public var usedB: Double?
    public var freeB: Double?
    public var latencyMs: Double?
    public var reachable: Bool?
    public var fsType: String?
    public var source: String?
    public var disk: String?
    public var smbVersion: String?
    public var mounted: Bool?
    public var checkedAt: Double?
    public var checkEveryMs: Double?
    public var answeredAt: Double?
}

public struct StatsNetwork: Codable, Sendable, Equatable {
    public var scope: String?
    public var interfaces: [StatsInterface]?
    public var today: StatsTraffic?
    public var week: StatsTraffic?
    public var month: StatsTraffic?
}

public struct StatsInterface: Codable, Sendable, Equatable {
    public var name: String?
    public var rxBps: Double?
    public var txBps: Double?
    public var speedMbps: Double?
}

public struct StatsTraffic: Codable, Sendable, Equatable { public var rxB: Double?; public var txB: Double? }

public struct StatsSensor: Codable, Sendable, Equatable {
    public var chip: String?
    public var label: String?
    /// 'temp' or 'fan'
    public var kind: String?
    public var value: Double?
}

public struct StatsProcesses: Codable, Sendable, Equatable {
    public var scope: String?
    public var top: [StatsProcess]?
}

public struct StatsProcess: Codable, Sendable, Equatable {
    public var pid: Double?
    public var name: String?
    public var cpu: Double?
    public var memB: Double?
    public var gpuVideo: Double?
}

public struct StatsAlert: Codable, Sendable, Equatable {
    public var key: String?
    public var level: String?
    public var message: String?
}

public struct StatsPrice: Codable, Sendable, Equatable {
    public var currency: String?
    public var mode: String?
    public var now: Double?
    public var rate: String?
}

public struct StatsEncoder: Codable, Sendable, Equatable {
    public var jobId: String?
    public var state: String?
    public var running: Bool?
    public var workers: [JSONValue]?
    public var concurrency: Double?
    public var device: StatsEncoderDevice?
    public var devices: [StatsEncoderLane]?
    public var run: StatsEncoderRun?
}

public struct StatsEncoderDevice: Codable, Sendable, Equatable {
    public var encoder: String?
    public var hardware: Bool?
    public var gpuName: String?
    public var node: String?
    public var decode: String?
}

public struct StatsEncoderLane: Codable, Sendable, Equatable {
    public var id: String?
    public var name: String?
    public var node: String?
    public var encoder: String?
    public var hardware: Bool?
    public var workers: Double?
    public var video: Double?
    public var busy: Double?
}

public struct StatsEncoderRun: Codable, Sendable, Equatable {
    public var wh: Double?
    public var avgW: Double?
    public var cost: Double?
    public var nodeWh: Double?
    public var clips: Double?
    public var savedB: Double?
    public var sinceWh: Double?
    public var cpuMeasured: Bool?
    public var startedAt: Double?
}

/// GET /api/stats/history
public struct StatsHistory: Codable, Sendable, Equatable {
    public var step: Double?
    public var start: Double?
    /// cpu, memory, gpuBusy, gpuVideo, powerW, rxBps, txBps, tempMax; null = no sample
    public var series: [String: [Double?]]?
}
