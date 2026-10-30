import Foundation

/// The six-camera sync clock's decisions (7.4). The app's SyncDriver reads
/// the players every 0.5 s while playing and does what this says; nothing
/// here touches AVFoundation.
public struct SyncPolicy: Sendable, Equatable {
    public struct CameraReading: Sendable, Equatable {
        public var ready: Bool
        public var likelyToKeepUp: Bool
        public var time: Double
        public init(ready: Bool, likelyToKeepUp: Bool, time: Double) { self.ready = ready; self.likelyToKeepUp = likelyToKeepUp; self.time = time }
    }

    public struct Reading: Sendable, Equatable {
        /// timeControlStatus == .waitingToPlayAtSpecifiedRate
        public var masterWaiting: Bool
        public var masterLikelyToKeepUp: Bool
        public var masterTime: Double
        public var cameras: [Camera: CameraReading]
        public var rate: Double
        public init(masterWaiting: Bool, masterLikelyToKeepUp: Bool, masterTime: Double, cameras: [Camera: CameraReading], rate: Double) {
            self.masterWaiting = masterWaiting; self.masterLikelyToKeepUp = masterLikelyToKeepUp
            self.masterTime = masterTime; self.cameras = cameras; self.rate = rate
        }
    }

    public enum Decision: Sendable, Equatable {
        /// the master stalled: stop the others (rate 0)
        case holdOthers
        /// the master plays again: start every player at this time (step 5)
        case restartAll(at: Double)
        /// one camera drifted or caught up: setRate(r, time:, atHostTime: now + lead)
        case realign(Camera, to: Double)
    }

    public var driftLimit: Double = 0.1
    public var hostLead: Double = 0.1
    public var stallTicks = 2
    private var waitingTicks = 0
    public private(set) var masterStalled = false
    private var stalledCameras = Set<Camera>()

    public init(driftLimit: Double = 0.1) { self.driftLimit = driftLimit }

    public mutating func tick(_ r: Reading) -> [Decision] {
        let masterBad = r.masterWaiting || !r.masterLikelyToKeepUp
        if masterBad {
            waitingTicks += 1
            if waitingTicks >= stallTicks && !masterStalled {
                masterStalled = true
                return [.holdOthers]
            }
            return []
        }
        waitingTicks = 0
        if masterStalled {
            masterStalled = false
            stalledCameras.removeAll()
            return [.restartAll(at: r.masterTime)]
        }
        var out: [Decision] = []
        let target = r.masterTime + hostLead * r.rate
        for cam in Camera.allCases {
            guard let c = r.cameras[cam], c.ready else { continue }
            if !c.likelyToKeepUp { stalledCameras.insert(cam); continue }
            if stalledCameras.remove(cam) != nil { out.append(.realign(cam, to: target)); continue }
            if abs(c.time - r.masterTime) > driftLimit { out.append(.realign(cam, to: target)) }
        }
        return out
    }

    /// prepare the standby set: near the end, and not right after the start
    public static func shouldPreload(local: Double, duration: Double, rate: Double) -> Bool {
        let remaining = duration - local
        return remaining <= 12 * rate && local >= min(3 * rate, duration / 2)
    }

    /// above 2x only when the item can play that fast
    public static func effectiveRate(_ asked: Double, canPlayFastForward: Bool) -> Double {
        asked > 2 && !canPlayFastForward ? 2 : asked
    }

    /// after a pause, a camera more than one frame off is moved to the master
    public static func needsPauseAlign(cameraTime: Double, masterTime: Double) -> Bool { abs(cameraTime - masterTime) > 0.03 }

    /// one frame step from the master's time
    public static func frameStep(from t: Double, by count: Int, fps: Double) -> Double {
        max(0, t + Double(count) / (fps > 0 ? fps : 36))
    }
}

/// One seek in flight per player group; while dragging only the newest
/// target waits (QA1820 chase time). Release seeks exactly.
public struct SeekCoalescer: Sendable, Equatable {
    public struct Seek: Sendable, Equatable {
        public var time: Double
        /// ±tolerance; 0 is exact
        public var tolerance: Double
    }

    public static let dragTolerance = 0.25
    private var inFlight = false
    private var waiting: Seek?

    public init() {}

    public var busy: Bool { inFlight }

    /// a new target: returns the seek to send now, or nil when one is out
    public mutating func request(_ time: Double, dragging: Bool) -> Seek? {
        let s = Seek(time: time, tolerance: dragging ? Self.dragTolerance : 0)
        if inFlight { waiting = s; return nil }
        inFlight = true
        return s
    }

    /// the seek finished: the newest waiting target goes next
    public mutating func completed() -> Seek? {
        if let w = waiting { waiting = nil; return w }
        inFlight = false
        return nil
    }

    public mutating func reset() { inFlight = false; waiting = nil }
}
