import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
import XCTest
@testable import SentryKit

/// A committed fixture file.
func fixture(_ name: String) throws -> Data {
    guard let url = Bundle.module.url(forResource: name, withExtension: nil, subdirectory: "Fixtures") else {
        throw XCTSkip("fixture \(name) missing")
    }
    return try Data(contentsOf: url)
}

func apiFixture(_ name: String) throws -> Data { try fixture("api/\(name)") }

/// An HTTP transport that answers from a script and keeps every request.
final class FakeTransport: HTTPTransport, @unchecked Sendable {
    struct Answer { var status: Int; var body: Data; var headers: [String: String] = [:] }
    private let _requests = Locked<[URLRequest]>([])
    private let handler: @Sendable (URLRequest) -> Answer

    init(_ handler: @escaping @Sendable (URLRequest) -> Answer) { self.handler = handler }

    var requests: [URLRequest] { _requests.get() }

    func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        _requests.update { $0.append(request) }
        let a = handler(request)
        if a.status == 0 { throw URLError(.cannotConnectToHost) }
        let res = HTTPURLResponse(url: request.url!, statusCode: a.status, httpVersion: "HTTP/1.1", headerFields: a.headers)!
        return (a.body, res)
    }
}

func json(_ s: String) -> Data { Data(s.utf8) }

/// The path part of a request, with the query.
extension URLRequest {
    var pathAndQuery: String {
        guard let u = url else { return "" }
        return u.path + (u.query.map { "?" + $0 } ?? "")
    }
    var bodyString: String { httpBody.map { String(decoding: $0, as: UTF8.self) } ?? "" }
}

/// Answers each /api path from the dumped fixtures.
func fixtureServer(_ routes: [String: String], fallback: Int = 404) -> FakeTransport {
    FakeTransport { req in
        let p = req.url?.path ?? ""
        if let name = routes[req.httpMethod! + " " + p] ?? routes[p], let d = try? apiFixture(name) {
            return .init(status: 200, body: d)
        }
        return .init(status: fallback, body: json(#"{"error":"Not found"}"#))
    }
}

func makeClient(_ t: HTTPTransport, token: String? = "TOKEN") -> APIClient {
    APIClient(baseURL: URL(string: "https://127.0.0.1:9000")!, transport: t, session: MemorySessionStore(token: token))
}

/// A file system made of entries, for the ownership checks.
struct FakeFS: FileChecking {
    var entries: [String: FileStatus] = [:]
    var files: [String: Data] = [:]
    var currentUID: UInt32 = 501
    var users: [String: UInt32] = ["_sentrysix": 290]
    func status(_ path: String) -> FileStatus? { entries[path] }
    func read(_ path: String) -> Data? { files[path] }
    func uid(ofUser name: String) -> UInt32? { users[name] }
}
