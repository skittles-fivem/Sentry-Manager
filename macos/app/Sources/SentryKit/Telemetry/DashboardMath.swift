import Foundation

/// The dashboard's numbers (7.6).
public enum DashboardMath {
    public enum SpeedUnit: String, Sendable { case mph = "MPH", kmh = "KM/H" }

    public static func speed(_ mps: Double, unit: SpeedUnit) -> Int {
        Int(JS.round(abs(mps) * (unit == .mph ? 2.23694 : 3.6)))
    }

    /// accelerator pedal: 0...1 or 0...100 from the car
    public static func acceleratorPercent(_ raw: Double) -> Double {
        raw > 1 ? min(100, raw) : min(100, raw * 100)
    }

    public static func acceleratorPressed(_ raw: Double) -> Bool { acceleratorPercent(raw) > 5 }

    public struct GForce: Sendable, Equatable {
        public var gX: Double
        public var gY: Double
        /// the dot inside the 60-point widget
        public var dotX: Double { 30 + gX * 25 }
        public var dotY: Double { 30 - gY * 25 }
        public var braking: Bool { gY < -0.3 }
        public var accelerating: Bool { gY > 0.3 }
        public var cornering: Bool { abs(gX) > 0.5 }
        public var xHigh: Bool { abs(gX) > 0.8 }
        public var yHigh: Bool { abs(gY) > 0.8 }
    }

    public static func gForce(ax: Double, ay: Double) -> GForce {
        let clamp = { (v: Double) in max(-2, min(2, v)) }
        return GForce(gX: clamp(ax / 9.81), gY: clamp(ay / 9.81))
    }

    public static let compassPoints = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"]

    public static func compass(_ heading: Double) -> (degrees: Int, point: String) {
        let r = JS.round(heading)
        var idx = Int(JS.round(heading / 45)) % 8
        if idx < 0 { idx += 8 }
        return (Int(r), compassPoints[idx])
    }

    /// autopilot highlight: self driving and autosteer only
    public static func autopilotHighlighted(_ state: UInt64) -> Bool { state == 1 || state == 2 }

    /// The steering wheel's smoothing: eases towards the target, snaps when close.
    public struct Steering: Sendable, Equatable {
        public private(set) var shown: Double
        private var drawn: Double

        public init(start: Double = 0) { shown = start; drawn = start }

        /// one display tick; returns true when the wheel should be redrawn
        public mutating func step(target: Double, dt: Double, rate: Double) -> Bool {
            let d = min(0.1, max(0, dt))
            let factor = 1 - exp(-45 * rate * d)
            shown += (target - shown) * factor
            if abs(target - shown) < 0.05 { shown = target }
            if abs(shown - drawn) >= 0.1 || (shown == target && drawn != target) {
                drawn = shown
                return true
            }
            return false
        }
    }
}

/// The route on the map (7.7): points, runs by autopilot, the fit box, the
/// event pin and the car's bearing.
public enum MapPath {
    public struct Point: Sendable, Equatable {
        public var lat: Double
        public var lon: Double
        public var autopilot: Bool
        public init(lat: Double, lon: Double, autopilot: Bool) { self.lat = lat; self.lon = lon; self.autopilot = autopilot }
    }

    public static func valid(lat: Double, lon: Double) -> Bool {
        lat.isFinite && lon.isFinite && !(lat == 0 && lon == 0) && abs(lat) <= 90 && abs(lon) <= 180
    }

    public static func points(_ track: TelemetryTrack) -> [Point] {
        track.samples.compactMap { s in
            valid(lat: s.record.latitude, lon: s.record.longitude)
                ? Point(lat: s.record.latitude, lon: s.record.longitude, autopilot: s.record.autopilot != 0) : nil
        }
    }

    public struct Run: Sendable, Equatable {
        public var autopilot: Bool
        public var points: [Point]
    }

    /// runs where autopilot changes, each sharing its first point with the last run
    public static func runs(_ pts: [Point]) -> [Run] {
        guard let first = pts.first else { return [] }
        var out = [Run(autopilot: first.autopilot, points: [first])]
        for p in pts.dropFirst() {
            if p.autopilot != out[out.count - 1].autopilot {
                let shared = out[out.count - 1].points.last!
                out.append(Run(autopilot: p.autopilot, points: [shared, p]))
            } else {
                out[out.count - 1].points.append(p)
            }
        }
        return out
    }

    public struct Box: Sendable, Equatable {
        public var minLat: Double, maxLat: Double, minLon: Double, maxLon: Double
        public var center: (lat: Double, lon: Double) { ((minLat + maxLat) / 2, (minLon + maxLon) / 2) }
        public static func == (a: Box, b: Box) -> Bool { a.minLat == b.minLat && a.maxLat == b.maxLat && a.minLon == b.minLon && a.maxLon == b.maxLon }
    }

    public static func box(_ pts: [Point]) -> Box? {
        guard let f = pts.first else { return nil }
        var b = Box(minLat: f.lat, maxLat: f.lat, minLon: f.lon, maxLon: f.lon)
        for p in pts { b.minLat = min(b.minLat, p.lat); b.maxLat = max(b.maxLat, p.lat); b.minLon = min(b.minLon, p.lon); b.maxLon = max(b.maxLon, p.lon) }
        return b
    }

    /// metres between two points (haversine)
    public static func distance(_ a: (lat: Double, lon: Double), _ b: (lat: Double, lon: Double)) -> Double {
        let r = 6_371_000.0
        let dLat = (b.lat - a.lat) * .pi / 180, dLon = (b.lon - a.lon) * .pi / 180
        let h = sin(dLat / 2) * sin(dLat / 2) + cos(a.lat * .pi / 180) * cos(b.lat * .pi / 180) * sin(dLon / 2) * sin(dLon / 2)
        return 2 * r * asin(min(1, sqrt(h)))
    }

    /// the event pin snaps to the nearest route point within 500 m
    public static func snapEventPin(lat: Double, lon: Double, route: [Point], within: Double = 500) -> (lat: Double, lon: Double) {
        var best: (Point, Double)?
        for p in route {
            let d = distance((lat, lon), (p.lat, p.lon))
            if best == nil || d < best!.1 { best = (p, d) }
        }
        if let b = best, b.1 <= within { return (b.0.lat, b.0.lon) }
        return (lat, lon)
    }

    /// the car arrow shows only with a record away from (0, 0)
    public static func arrowVisible(_ r: SeiRecord?) -> Bool {
        guard let r else { return false }
        return abs(r.latitude) >= 0.001 && abs(r.longitude) >= 0.001
    }

    /// bearing smoothing the shortest way round: from `current` towards `target`
    public static func smoothBearing(current: Double, target: Double, factor: Double) -> Double {
        var delta = (target - current).truncatingRemainder(dividingBy: 360)
        if delta > 180 { delta -= 360 }
        if delta < -180 { delta += 360 }
        var out = (current + delta * factor).truncatingRemainder(dividingBy: 360)
        if out < 0 { out += 360 }
        return out
    }

    /// how long the arrow's move is animated at a rate
    public static func arrowAnimation(rate: Double) -> Double { max(0.03, 0.15 / max(rate, 0.0001)) }
}
