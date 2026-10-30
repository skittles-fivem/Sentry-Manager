import Foundation

/// One server-sent event.
public struct SSEEvent: Sendable, Equatable {
    public var event: String
    public var data: String
    public var id: String?

    public init(event: String = "message", data: String, id: String? = nil) { self.event = event; self.data = data; self.id = id }

    /// the data as JSON
    public func decode<T: Decodable>(_ type: T.Type) -> T? { try? JSON.decode(type, from: Data(data.utf8)) }
}

/// A line parser for text/event-stream, fed with chunks as they arrive.
/// CR, LF and CRLF end lines, also when a CRLF is split across chunks.
public struct SSEParser: Sendable {
    private var line = [UInt8]()
    private var afterCR = false
    private var started = false
    private var eventName: String?
    private var dataLines: [String] = []
    private var hasData = false
    private var lastID: String?
    /// comments (": ping") seen so far; they keep a stream alive
    public private(set) var comments = 0
    /// a retry: field, in milliseconds
    public private(set) var retryMs: Int?

    public init() {}

    public mutating func feed(_ data: Data) -> [SSEEvent] {
        var out: [SSEEvent] = []
        for b in data {
            if afterCR {
                afterCR = false
                if b == 0x0a { continue }
            }
            if b == 0x0d { afterCR = true; endLine(&out); continue }
            if b == 0x0a { endLine(&out); continue }
            line.append(b)
        }
        return out
    }

    /// the end of the stream: a last event without its blank line is dropped
    public mutating func finish() {
        line.removeAll(); afterCR = false; eventName = nil; dataLines = []; hasData = false
    }

    private mutating func endLine(_ out: inout [SSEEvent]) {
        var bytes = line
        line.removeAll(keepingCapacity: true)
        if !started {
            started = true
            if bytes.starts(with: [0xef, 0xbb, 0xbf]) { bytes.removeFirst(3) }
        }
        if bytes.isEmpty {
            if hasData {
                out.append(SSEEvent(event: (eventName?.isEmpty == false) ? eventName! : "message", data: dataLines.joined(separator: "\n"), id: lastID))
            }
            eventName = nil; dataLines = []; hasData = false
            return
        }
        if bytes.first == 0x3a { comments += 1; return }
        let text = String(decoding: bytes, as: UTF8.self)
        let field: Substring
        var value: Substring
        if let colon = text.firstIndex(of: ":") {
            field = text[..<colon]
            value = text[text.index(after: colon)...]
            if value.first == " " { value = value.dropFirst() }
        } else {
            field = Substring(text); value = ""
        }
        switch field {
        case "event": eventName = String(value)
        case "data": dataLines.append(String(value)); hasData = true
        case "id": if !value.contains("\0") { lastID = String(value) }
        case "retry": if let n = Int(value), n >= 0 { retryMs = n }
        default: break
        }
    }
}
