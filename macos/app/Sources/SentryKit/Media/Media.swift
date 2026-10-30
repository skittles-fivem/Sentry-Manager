import Foundation

/// `Content-Range: bytes a-b/TOTAL` (or `bytes */TOTAL` on a 416).
public struct ContentRange: Sendable, Equatable {
    public var start: Int64?
    public var end: Int64?
    public var total: Int64?

    public init(start: Int64?, end: Int64?, total: Int64?) { self.start = start; self.end = end; self.total = total }

    public static func parse(_ header: String?) -> ContentRange? {
        guard let h = header?.trimmingCharacters(in: .whitespaces), h.lowercased().hasPrefix("bytes") else { return nil }
        let rest = h.dropFirst(5).trimmingCharacters(in: .whitespaces)
        let parts = rest.split(separator: "/", maxSplits: 1, omittingEmptySubsequences: false)
        guard parts.count == 2 else { return nil }
        let total: Int64? = parts[1] == "*" ? nil : Int64(parts[1])
        if parts[1] != "*" && total == nil { return nil }
        if parts[0] == "*" { return ContentRange(start: nil, end: nil, total: total) }
        let se = parts[0].split(separator: "-", maxSplits: 1, omittingEmptySubsequences: false)
        guard se.count == 2, let s = Int64(se[0]), let e = Int64(se[1]), s >= 0, e >= s else { return nil }
        if let t = total, e >= t { return nil }
        return ContentRange(start: s, end: e, total: total)
    }

    public var length: Int64? { start.flatMap { s in end.map { $0 - s + 1 } } }
}

/// Cuts a byte range into requests the server answers whole: it caps every
/// 206 at 2 MiB, whatever was asked (7.3).
public enum RangePlanner {
    public static let chunk: Int64 = 2 * 1024 * 1024

    /// The ranges to ask for, inclusive ends. `length` nil with
    /// `toEnd` means "to the end of the file"; `total` nil = not known yet.
    public static func plan(offset: Int64, length: Int64?, toEnd: Bool, total: Int64?) -> [ClosedRange<Int64>] {
        var end: Int64
        if toEnd || length == nil {
            guard let total else { return [offset...(offset + chunk - 1)] }
            end = total - 1
        } else {
            end = offset + length! - 1
            if let total { end = min(end, total - 1) }
        }
        guard end >= offset else { return [] }
        var out: [ClosedRange<Int64>] = []
        var s = offset
        while s <= end {
            let e = min(end, s + chunk - 1)
            out.append(s...e)
            s = e + 1
        }
        return out
    }

    public static func header(_ r: ClosedRange<Int64>) -> String { "bytes=\(r.lowerBound)-\(r.upperBound)" }
}

/// What a media answer means for the loader (7.3, "Answers").
public enum MediaAnswer: Sendable, Equatable {
    case data
    /// a 200 where a range was asked: the file changed (converted in place)
    case fileChanged
    case signIn
    case outsideShares
    case gone
    case finished
    case failed(Int)

    public static func classify(status: Int, askedRange: Bool) -> MediaAnswer {
        switch status {
        case 206: return .data
        case 200: return askedRange ? .fileChanged : .data
        case 401: return .signIn
        case 403: return .outsideShares
        case 404: return .gone
        case 416: return .finished
        default: return .failed(status)
        }
    }

    public var message: String? {
        switch self {
        case .outsideShares: return "Outside the vehicle shares."
        case .gone: return "This clip is no longer there"
        default: return nil
        }
    }
}

/// The custom scheme AVFoundation sees, and the HTTPS URL behind it.
public enum MediaURL {
    public static let scheme = "sentry-media"

    public static func asset(path: String) -> URL {
        URL(string: "\(scheme)://media?path=\(PathEncoding.component(path))")!
    }

    /// sentry-media://media?path=X -> /api/media?path=X (X kept as encoded)
    public static func apiPathAndQuery(for url: URL) -> String? {
        guard url.scheme == scheme, url.host == "media", let q = url.query, q.hasPrefix("path=") else { return nil }
        let encoded = String(q.dropFirst(5))
        guard let decoded = encoded.removingPercentEncoding, decoded.hasPrefix("/") else { return nil }
        return "/api/media?path=\(PathEncoding.component(decoded))"
    }

    public static func contentType(path: String) -> String {
        path.lowercased().hasSuffix(".mov") ? "com.apple.quicktime-movie" : "public.mpeg-4"
    }
}
