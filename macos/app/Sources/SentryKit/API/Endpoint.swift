import Foundation

public enum HTTPMethod: String, Sendable {
    case get = "GET", head = "HEAD", post = "POST", put = "PUT", delete = "DELETE"
}

/// How long a request may take (2.5 of the contract).
public enum RequestTimeout: Sendable, Equatable {
    case polled      // 10 s: stats polled on a timer
    case history     // 15 s: history and logs
    case normal      // 30 s
    case benchmark   // 6 h: runBenchmark stays open for minutes
    case custom(TimeInterval)

    public var seconds: TimeInterval {
        switch self {
        case .polled: return 10
        case .history: return 15
        case .normal: return 30
        case .benchmark: return 6 * 3600
        case .custom(let s): return s
        }
    }
}

/// One API call: method, path, query and body, and the answer's type.
public struct Endpoint<Response: Decodable & Sendable>: Sendable {
    public var method: HTTPMethod
    /// already percent-encoded where it carries parameters
    public var path: String
    public var query: [(String, String)]
    public var body: Data?
    public var timeout: RequestTimeout
    /// extra headers (X-Client-Id for the converter)
    public var headers: [String: String]

    public init(_ method: HTTPMethod, _ path: String, query: [(String, String)] = [], body: Data? = nil,
                timeout: RequestTimeout = .normal, headers: [String: String] = [:]) {
        self.method = method; self.path = path; self.query = query; self.body = body; self.timeout = timeout; self.headers = headers
    }

    public init<B: Encodable>(_ method: HTTPMethod, _ path: String, query: [(String, String)] = [], json: B,
                              timeout: RequestTimeout = .normal, headers: [String: String] = [:]) {
        self.init(method, path, query: query, body: (try? JSON.encode(json)) ?? Data("{}".utf8), timeout: timeout, headers: headers)
    }

    /// path plus the encoded query
    public var pathAndQuery: String {
        guard !query.isEmpty else { return path }
        return path + "?" + query.map { "\(PathEncoding.component($0.0))=\(PathEncoding.component($0.1))" }.joined(separator: "&")
    }
}

/// Percent-encoding for path parameters and query values: everything but
/// the unreserved characters, so "/" and "?" in an id never change the path.
public enum PathEncoding {
    static let unreserved: CharacterSet = {
        var s = CharacterSet()
        s.insert(charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")
        return s
    }()

    public static func component(_ s: String) -> String {
        s.addingPercentEncoding(withAllowedCharacters: unreserved) ?? s
    }
}
