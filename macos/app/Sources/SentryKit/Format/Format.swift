import Foundation

/// The built-in interface's formatters (web/app.js), ported exactly. The
/// tests compare them with the web functions' own output.
public enum Format {
    static func fin(_ n: Double?) -> Bool { n.map { $0.isFinite } ?? false }

    /// 1.5 KiB, 2.0 MiB (the Footage and Recycle Bin sizes)
    public static func bytes(_ n: Double?) -> String {
        guard let n, n.isFinite else { return "—" }
        let u = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"]
        var v = n
        var i = 0
        while v >= 1024 && i < u.count - 1 { v /= 1024; i += 1 }
        return "\(JS.toFixed(v, i == 0 ? 0 : 1)) \(u[i])"
    }

    /// "1,234 recordings", "1 recording"
    public static func plural(_ n: Double?, _ word: String) -> String {
        let v = n ?? 0
        return "\(JS.localeString(v)) \(word)\(v == 1 ? "" : "s")"
    }

    /// how long a job took: 42 s, 12 m 5 s, 8 h 0 m
    public static func took(_ ms: Double) -> String {
        let s = Int(max(0, JS.round(ms / 1000)))
        if s < 60 { return "\(s) s" }
        if s < 3600 { return "\(s / 60) m \(s % 60) s" }
        return "\(s / 3600) h \((s % 3600) / 60) m"
    }

    /// "just now", "5 min ago", "3 h ago", then the date
    public static func ago(_ ts: Double?, now: Double, timeZone: TimeZone = .current) -> String {
        guard let ts, ts != 0 else { return "never" }
        let s = JS.round((now - ts) / 1000)
        if s < 60 { return "just now" }
        if s < 3600 { return "\(JS.string(JS.round(s / 60))) min ago" }
        if s < 86400 { return "\(JS.string(JS.round(s / 3600))) h ago" }
        return dateText(ts, timeZone: timeZone)
    }

    /// toLocaleDateString() in English: 6/1/2025
    public static func dateText(_ ms: Double, timeZone: TimeZone = .current) -> String {
        let c = components(ms, timeZone)
        return "\(c.month!)/\(c.day!)/\(c.year!)"
    }

    /// toLocaleString() in English on the chosen clock: "6/1/2025, 8:00:00 AM" or "6/1/2025, 08:00:00"
    public static func when(_ ms: Double?, clock24: Bool, timeZone: TimeZone = .current) -> String {
        guard let ms, ms != 0 else { return "—" }
        let c = components(ms, timeZone)
        let date = "\(c.month!)/\(c.day!)/\(c.year!)"
        let mm = String(format: "%02d", c.minute!), ss = String(format: "%02d", c.second!)
        if clock24 { return "\(date), \(String(format: "%02d", c.hour!)):\(mm):\(ss)" }
        let h = c.hour!
        return "\(date), \(h % 12 == 0 ? 12 : h % 12):\(mm):\(ss) \(h < 12 ? "AM" : "PM")"
    }

    static func components(_ ms: Double, _ tz: TimeZone) -> DateComponents {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = tz
        return cal.dateComponents([.year, .month, .day, .hour, .minute, .second], from: Date(timeIntervalSince1970: ms / 1000))
    }

    /// a recording's wall-clock text on the chosen clock: "2026-10-29 3:10:02 PM"
    public static func clockText(_ s: String?, clock24: Bool) -> String {
        let str = s ?? ""
        guard !clock24 else { return str }
        let re = try! NSRegularExpression(pattern: "(\\d{1,2}):(\\d{2})(:\\d{2})?$")
        guard let m = re.firstMatch(in: str, range: NSRange(str.startIndex..., in: str)),
              let all = Range(m.range, in: str), let hh = Range(m.range(at: 1), in: str), let mm = Range(m.range(at: 2), in: str) else { return str }
        let ss = Range(m.range(at: 3), in: str).map { String(str[$0]) } ?? ""
        let h = Int(str[hh])!
        return str.replacingCharacters(in: all, with: "\(h % 12 == 0 ? 12 : h % 12):\(str[mm])\(ss) \(h < 12 ? "AM" : "PM")")
    }

    /// seconds since midnight as 08:01:05, or 8:01:05 AM
    public static func timeHMS(_ seconds: Int, clock24: Bool) -> String {
        let s = ((seconds % 86400) + 86400) % 86400
        let h = s / 3600, m = (s % 3600) / 60, sec = s % 60
        if clock24 { return String(format: "%02d:%02d:%02d", h, m, sec) }
        return String(format: "%d:%02d:%02d %@", h % 12 == 0 ? 12 : h % 12, m, sec, h < 12 ? "AM" : "PM")
    }

    /// "sentry_aware_object_detection" -> "Sentry aware object detection"
    public static func humanize(_ s: String?) -> String {
        let t = (s ?? "").replacingOccurrences(of: "_", with: " ")
        guard let f = t.first, f.isASCII, f.isLetter || f.isNumber else { return t }
        return f.uppercased() + t.dropFirst()
    }

    // MARK: Overview numbers

    public static let sizeUnits = ["B", "KB", "MB", "GB", "TB", "PB"]

    public static func clampPct(_ n: Double?) -> Double {
        let v = fin(n) ? n! : 0
        return max(0, min(100, JS.round(v * 10) / 10))
    }

    static func sizeUnit(_ b: Double) -> Int {
        var i = 0
        var v = abs(b)
        while v >= 1024 && i < sizeUnits.count - 1 { v /= 1024; i += 1 }
        return i
    }

    static func sizeNum(_ b: Double, _ i: Int) -> String {
        let v = b / pow(1024, Double(i))
        return v >= 100 || i == 0 ? JS.string(JS.round(v)) : oneDecimal(v)
    }

    /// toFixed(1) with a trailing ".0" taken off
    static func oneDecimal(_ v: Double) -> String {
        let s = JS.toFixed(v, 1)
        return s.hasSuffix(".0") ? String(s.dropLast(2)) : s
    }

    /// 18.2 GB, 640 MB
    public static func size(_ b: Double?) -> String {
        guard let b, b.isFinite else { return "—" }
        return "\(sizeNum(b, sizeUnit(b))) \(sizeUnits[sizeUnit(b)])"
    }

    /// "112 of 476 GB", "1.1 / 6 GB"
    public static func sizeOf(_ used: Double, _ total: Double, sep: String = " of ") -> String {
        let i = sizeUnit(total)
        return "\(sizeNum(used, i))\(sep)\(sizeNum(total, i)) \(sizeUnits[i])"
    }

    public static func rate(_ bps: Double?) -> String {
        guard let bps, bps.isFinite else { return "—" }
        if bps > 0 && bps < 1024 * 1024 { return "\(JS.string(max(1, JS.round(bps / 1024)))) KB/s" }
        let i = max(2, sizeUnit(bps))
        return "\(sizeNum(bps, i)) \(sizeUnits[i])/s"
    }

    public static func pct(_ n: Double) -> String { "\(JS.string(JS.round(n)))%" }

    public static func watts(_ w: Double) -> String { "\(w >= 10 ? JS.string(JS.round(w)) : oneDecimal(w)) W" }

    public static func temp(_ c: Double) -> String { "\(JS.string(JS.round(c))) °C" }

    public static func clock(_ mhz: Double) -> String {
        mhz >= 1000 ? "\(JS.trimmedFixed(mhz / 1000, 2)) GHz" : "\(JS.string(JS.round(mhz))) MHz"
    }

    /// "12.4 Wh", "412 Wh", "5.2 kWh"
    public static func energyText(_ wh: Double) -> String {
        let tenth = JS.round(wh * 10) / 10
        if tenth < 100 { return "\(JS.toFixed(tenth, 1)) Wh" }
        if JS.round(wh) < 1000 { return "\(JS.string(JS.round(wh))) Wh" }
        let k = wh / 1000
        return "\(JS.round(k * 10) / 10 < 100 ? JS.toFixed(k, 1) : JS.localeString(JS.round(k))) kWh"
    }

    /// $0.14 but 1.20 kr
    public static func inCurrency(_ n: String, _ cur: String?) -> String {
        let c = (cur?.isEmpty ?? true) ? "$" : cur!
        let letters = !c.isEmpty && c.unicodeScalars.allSatisfy { ("a"..."z").contains($0) || ("A"..."Z").contains($0) }
        return letters ? "\(n) \(c)" : "\(c)\(n)"
    }

    /// "≈ $0.14"
    public static func money(_ v: Double, _ cur: String?) -> String {
        "≈ \(inCurrency(v >= 100 ? JS.localeString(JS.round(v)) : JS.toFixed(v, 2), cur))"
    }

    /// uptime: "3 d 4 h", "2 h 5 m", "7 m", "12 s"
    public static func span(_ s: Double) -> String {
        let d = Int(floor(s / 86400))
        let h = Int(floor(s.truncatingRemainder(dividingBy: 86400) / 3600))
        let m = Int(floor(s.truncatingRemainder(dividingBy: 3600) / 60))
        if d != 0 { return "\(d) d \(h) h" }
        if h != 0 { return "\(h) h \(m) m" }
        if m != 0 { return "\(m) m" }
        return "\(JS.string(max(0, JS.round(s)))) s"
    }

    /// a share's answer time
    public static func answer(_ ms: Double) -> String {
        if ms < 1 { return "under 1 ms" }
        if ms < 1000 { return "\(JS.string(JS.round(ms))) ms" }
        return "\(oneDecimal(ms / 1000)) s"
    }

    public static func linkSpeed(_ mbps: Double?) -> String {
        guard let mbps, mbps.isFinite, mbps > 0 else { return "" }
        return mbps >= 1000 ? "\(JS.trimmedFixed(mbps / 1000, 1)) Gbit/s" : "\(JS.string(JS.round(mbps))) Mbit/s"
    }

    /// "Intel(R) Core(TM) i5 CPU @ 3.2GHz" -> "Intel Core i5"
    public static func cpuName(_ m: String?) -> String {
        var s = m ?? ""
        s = s.replacingOccurrences(of: "\\((R|TM)\\)", with: "", options: [.regularExpression, .caseInsensitive])
        s = s.replacingOccurrences(of: "\\s+CPU\\s+@.*$", with: "", options: [.regularExpression, .caseInsensitive])
        s = s.replacingOccurrences(of: "\\s+with\\s+Radeon.*$", with: "", options: [.regularExpression, .caseInsensitive])
        s = s.replacingOccurrences(of: "\\s+", with: " ", options: .regularExpression)
        return s.trimmingCharacters(in: .whitespaces)
    }

    /// a sensor's plain name
    public static func sensorName(chip: String?, label: String?, kind: String?, multiSocket: Bool = false) -> String {
        let chip = chip ?? "", label = label ?? ""
        func match(_ p: String, _ s: String) -> [String]? {
            let re = try! NSRegularExpression(pattern: p)
            guard let m = re.firstMatch(in: s, range: NSRange(s.startIndex..., in: s)) else { return nil }
            return (0..<m.numberOfRanges).map { Range(m.range(at: $0), in: s).map { String(s[$0]) } ?? "" }
        }
        if chip == "coretemp", let m = match("^Package id (\\d+)$", label) {
            if multiSocket { return "CPU \(Int(m[1])! + 1) package" }
            return m[1] == "0" ? "CPU package" : "CPU package \(m[1])"
        }
        if chip == "k10temp", match("^T(ctl|die)$", label) != nil { return "CPU (\(label))" }
        if chip == "k10temp", let m = match("^Tccd(\\d+)$", label) { return "CPU die \(m[1])" }
        if match("^cpu[-_]thermal$", chip) != nil || match("^cpu[-_]thermal$", label) != nil { return "CPU" }
        if chip == "nvme" { return label == "Composite" || label.isEmpty ? "NVMe" : "NVMe \(label.lowercased())" }
        if match("^(amdgpu|i915|xe|nouveau|radeon)$", chip) != nil {
            if kind == "fan" { return "GPU fan" }
            return ["edge": "GPU", "pkg": "GPU", "junction": "GPU hotspot", "mem": "GPU memory", "vram": "GPU memory"][label] ?? "GPU \(label)"
        }
        if let m = match("^fan(\\d+)$", label) { return "Fan \(m[1])" }
        if let m = match("^temp(\\d+)$", label) { return "\(chip) \(m[1])" }
        return !label.isEmpty ? label : (!chip.isEmpty ? chip : "Sensor")
    }

    /// whether a sensor reads hot
    public static func sensorHot(chip: String?, kind: String?, value: Double, name: String) -> Bool {
        guard kind == "temp" else { return false }
        let limit: Double = chip == "drivetemp" ? 55 : chip == "nvme" ? 70 : (name.hasPrefix("CPU") || name.hasPrefix("GPU")) ? 90 : 85
        return value >= limit
    }
}
