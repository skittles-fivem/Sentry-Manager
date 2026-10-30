import Foundation

/// JavaScript's number-to-text rules, so the app prints what the web prints.
public enum JS {
    /// Math.round: halves go up (towards +infinity).
    public static func round(_ x: Double) -> Double {
        guard x.isFinite else { return x }
        let down = x.rounded(.down)
        return x - down >= 0.5 ? down + 1 : down
    }

    /// Number.prototype.toFixed(digits) for |x| < 1e21: the nearest decimal,
    /// and on an exact tie the larger one.
    public static func toFixed(_ x: Double, _ digits: Int) -> String {
        guard x.isFinite else { return x.isNaN ? "NaN" : (x < 0 ? "-Infinity" : "Infinity") }
        if abs(x) >= 1e21 { return String(x) }
        let negative = x < 0
        // the exact decimal value of the double (glibc and Apple's libc both
        // print every digit), then round half up by hand
        let exact = String(format: "%.400f", abs(x))
        let parts = exact.split(separator: ".", omittingEmptySubsequences: false)
        var intDigits = Array(parts[0]).map { Int(String($0))! }
        let frac = parts.count > 1 ? Array(parts[1]).map { Int(String($0))! } : []
        var kept = Array(frac.prefix(digits))
        while kept.count < digits { kept.append(0) }
        let next = digits < frac.count ? frac[digits] : 0
        if next >= 5 {
            // carry
            var i = kept.count - 1
            var carry = 1
            while carry == 1 && i >= 0 {
                kept[i] += 1
                if kept[i] == 10 { kept[i] = 0; i -= 1 } else { carry = 0 }
            }
            if carry == 1 {
                var j = intDigits.count - 1
                while carry == 1 && j >= 0 {
                    intDigits[j] += 1
                    if intDigits[j] == 10 { intDigits[j] = 0; j -= 1 } else { carry = 0 }
                }
                if carry == 1 { intDigits.insert(1, at: 0) }
            }
        }
        var text = intDigits.map(String.init).joined()
        if digits > 0 { text += "." + kept.map(String.init).joined() }
        // JS keeps the sign even when the digits round to zero: (-0.04).toFixed(1) is "-0.0"
        return negative ? "-" + text : text
    }

    /// String(n) for the numbers the web prints that way (integers and
    /// short fractions): 3, 2.5, 1.25.
    public static func string(_ x: Double) -> String {
        guard x.isFinite else { return x.isNaN ? "NaN" : (x < 0 ? "-Infinity" : "Infinity") }
        if x == x.rounded() && abs(x) < 1e21 { return String(format: "%.0f", x) }
        // the shortest text that reads back as the same double, as JS does
        var s = "\(x)"
        if s.hasSuffix(".0") { s.removeLast(2) }
        return s
    }

    /// Number.prototype.toLocaleString() in English: groups of three, at
    /// most three decimals, rounded half away from zero.
    public static func localeString(_ x: Double, maxFraction: Int = 3) -> String {
        guard x.isFinite else { return x.isNaN ? "NaN" : (x < 0 ? "-∞" : "∞") }
        // ICU rounds the shortest decimal form, half away from zero
        var fixed = roundShortest(x, maxFraction)
        if fixed.contains(".") {
            while fixed.hasSuffix("0") { fixed.removeLast() }
            if fixed.hasSuffix(".") { fixed.removeLast() }
        }
        let negative = fixed.hasPrefix("-")
        if negative { fixed.removeFirst() }
        let parts = fixed.split(separator: ".", omittingEmptySubsequences: false)
        let int = Array(parts[0])
        var grouped = ""
        for (i, ch) in int.enumerated() {
            if i > 0 && (int.count - i) % 3 == 0 { grouped.append(",") }
            grouped.append(ch)
        }
        let out = parts.count > 1 ? grouped + "." + parts[1] : grouped
        return negative && out != "0" ? "-" + out : out
    }

    /// the shortest decimal text of x, rounded half up to `digits` places
    static func roundShortest(_ x: Double, _ digits: Int) -> String {
        let text = "\(abs(x))"
        guard !text.contains("e"), let dot = text.firstIndex(of: ".") else { return toFixed(x, digits) }
        let intPart = String(text[..<dot])
        let frac = Array(text[text.index(after: dot)...])
        if frac.count <= digits { return (x < 0 ? "-" : "") + text }
        // decide on the first dropped digit, then let toFixed carry on an exact value
        let kept = intPart + "." + String(frac.prefix(digits))
        let up = Int(String(frac[digits]))! >= 5
        let base = Double(kept)!
        let step = pow(10, -Double(digits))
        let rounded = up ? toFixed(base + step, digits) : toFixed(base, digits)
        return (x < 0 && Double(rounded) != 0 ? "-" : "") + rounded
    }

    /// Number(x.toFixed(d)) printed back: 2.50 -> "2.5", 3.00 -> "3".
    public static func trimmedFixed(_ x: Double, _ digits: Int) -> String {
        var s = toFixed(x, digits)
        if s.contains(".") {
            while s.hasSuffix("0") { s.removeLast() }
            if s.hasSuffix(".") { s.removeLast() }
        }
        if s == "-0" { return "0" }
        return s
    }
}
