import Foundation

/// One Tesla telemetry record (dashcam.proto SeiMetadata), proto3 defaults.
public struct SeiRecord: Sendable, Equatable {
    public var version: UInt64 = 0
    /// 0 park, 1 drive, 2 reverse, 3 neutral
    public var gear: UInt64 = 0
    public var frameSeqNo: UInt64 = 0
    public var speedMps: Float = 0
    public var accelerator: Float = 0
    public var steeringDeg: Float = 0
    public var blinkerLeft = false
    public var blinkerRight = false
    public var brake = false
    /// 0 none, 1 self driving, 2 autosteer, 3 TACC
    public var autopilot: UInt64 = 0
    public var latitude: Double = 0
    public var longitude: Double = 0
    public var heading: Double = 0
    public var accelX: Double = 0
    public var accelY: Double = 0
    public var accelZ: Double = 0

    public init() {}
}

/// A hand-written protobuf reader for SeiMetadata (7.5): varints 1-3 and
/// 7-10, little-endian floats 4-6, doubles 11-16. Length-delimited fields
/// are skipped; an unknown wire type ends the record, keeping what was read.
public enum ProtoReader {
    public static func decode(_ bytes: [UInt8]) -> SeiRecord {
        var r = SeiRecord()
        var i = 0
        func varint() -> UInt64? {
            var v: UInt64 = 0
            var shift: UInt64 = 0
            while i < bytes.count {
                let b = bytes[i]; i += 1
                if shift < 64 { v |= UInt64(b & 0x7f) << shift }
                if b & 0x80 == 0 { return v }
                shift += 7
            }
            return nil
        }
        while i < bytes.count {
            guard let key = varint() else { break }
            let field = key >> 3
            let wire = key & 7
            switch wire {
            case 0:
                guard let v = varint() else { return r }
                switch field {
                case 1: r.version = v
                case 2: r.gear = v
                case 3: r.frameSeqNo = v
                case 7: r.blinkerLeft = v != 0
                case 8: r.blinkerRight = v != 0
                case 9: r.brake = v != 0
                case 10: r.autopilot = v
                default: break
                }
            case 5:
                guard i + 4 <= bytes.count else { return r }
                let bits = UInt32(bytes[i]) | UInt32(bytes[i + 1]) << 8 | UInt32(bytes[i + 2]) << 16 | UInt32(bytes[i + 3]) << 24
                i += 4
                let f = Float(bitPattern: bits)
                switch field {
                case 4: r.speedMps = f
                case 5: r.accelerator = f
                case 6: r.steeringDeg = f
                default: break
                }
            case 1:
                guard i + 8 <= bytes.count else { return r }
                var bits: UInt64 = 0
                for k in 0..<8 { bits |= UInt64(bytes[i + k]) << (UInt64(k) * 8) }
                i += 8
                let d = Double(bitPattern: bits)
                switch field {
                case 11: r.latitude = d
                case 12: r.longitude = d
                case 13: r.heading = d
                case 14: r.accelX = d
                case 15: r.accelY = d
                case 16: r.accelZ = d
                default: break
                }
            case 2:
                guard let len = varint(), len <= UInt64(bytes.count - i) else { return r }
                i += Int(len)
            default:
                return r
            }
        }
        return r
    }

    public static func decode(base64: String) -> SeiRecord? {
        guard let d = Data(base64Encoded: base64) else { return nil }
        return decode([UInt8](d))
    }
}

/// A clip's records in time order, with the two lookups the skins use.
public struct TelemetryTrack: Sendable, Equatable {
    public struct Sample: Sendable, Equatable {
        public var ms: Double
        public var record: SeiRecord
    }

    public private(set) var samples: [Sample]
    public static let nearestWindowMs: Double = 3000

    /// from GET /api/telemetry: frames come in decode order, so they are sorted
    public init(_ answer: TelemetryAnswer) {
        let list = (answer.frames ?? []).compactMap { f in ProtoReader.decode(base64: f.record).map { Sample(ms: f.tsMs, record: $0) } }
        samples = list.enumerated().sorted { a, b in a.element.ms == b.element.ms ? a.offset < b.offset : a.element.ms < b.element.ms }.map(\.element)
    }

    public init(samples: [Sample]) { self.samples = samples.sorted { $0.ms < $1.ms } }

    /// index of the last sample with ms <= t, or nil
    public func lastIndex(atOrBefore t: Double) -> Int? {
        var lo = 0, hi = samples.count - 1, found: Int?
        while lo <= hi {
            let mid = (lo + hi) / 2
            if samples[mid].ms <= t { found = mid; lo = mid + 1 } else { hi = mid - 1 }
        }
        return found
    }

    /// the built-in viewer's rule: the nearer of the two around t, nil past 3 s
    public func nearest(_ t: Double, window: Double = nearestWindowMs) -> SeiRecord? {
        guard !samples.isEmpty else { return nil }
        let i = lastIndex(atOrBefore: t)
        var best: Sample?
        if let i { best = samples[i] }
        let nextIndex = (i ?? -1) + 1
        if nextIndex < samples.count {
            let n = samples[nextIndex]
            if best == nil || abs(n.ms - t) < abs(best!.ms - t) { best = n }
        }
        guard let b = best, abs(b.ms - t) <= window else { return nil }
        return b.record
    }

    /// the packs' rule: the last record at or before t
    public func atOrBefore(_ t: Double) -> SeiRecord? { lastIndex(atOrBefore: t).map { samples[$0].record } }

    /// frames per second from the median spacing; 36 when it cannot tell
    public var fps: Double {
        guard samples.count > 2 else { return 36 }
        var gaps: [Double] = []
        for i in 1..<samples.count { let g = samples[i].ms - samples[i - 1].ms; if g > 0 { gaps.append(g) } }
        guard !gaps.isEmpty else { return 36 }
        gaps.sort()
        let m = gaps[gaps.count / 2]
        return m > 0 ? 1000 / m : 36
    }
}
