import Foundation

/// The LaunchAgent job, as data. The bundled plist (bundle/...agent.plist,
/// for SMAppService) uses BundleProgram; the hand-written fallback for
/// builds SMAppService refuses (3.2) uses the launcher's absolute path.
public enum AgentPlist {
    public static func job(program: String? = nil) -> [String: Any] {
        var job: [String: Any] = [
            "Label": ServiceNames.agentLabel,
            "RunAtLoad": true,
            "KeepAlive": ["SuccessfulExit": false],
            "ThrottleInterval": 5,
            "ExitTimeOut": 60,
            "ProcessType": "Interactive",
            // 027, which launchd takes in decimal
            "Umask": 23,
            "SoftResourceLimits": ["NumberOfFiles": 8192],
            "HardResourceLimits": ["NumberOfFiles": 8192],
            "AssociatedBundleIdentifiers": [ServiceNames.bundleID],
        ]
        if let program {
            job["Program"] = program
        } else {
            job["BundleProgram"] = "Contents/MacOS/sentry-six-engine"
        }
        return job
    }

    public static func xml(program: String? = nil) throws -> Data {
        try PropertyListSerialization.data(fromPropertyList: job(program: program), format: .xml, options: 0)
    }
}

/// The parts of engine.json (2.3) the service side reads: the port to ask
/// /healthz on and the version to compare with the app's own engine.
public struct PublicEngineFile: Decodable, Sendable, Equatable {
    public struct TLS: Decodable, Sendable, Equatable {
        public let mode: String
        public let caPem: String?
        public let caSha256: String?
        public let leafPem: String?
        public let leafSha256: String?
    }

    public let v: Int
    public let port: Int
    public let pid: Int?
    public let version: String?
    public let sha: String?
    public let tls: TLS?

    public static func parse(_ data: Data) -> PublicEngineFile? {
        guard let f = try? JSONDecoder().decode(PublicEngineFile.self, from: data), f.v == 1, (1...65535).contains(f.port) else { return nil }
        return f
    }

    /// The DER bytes of a PEM certificate, for SecCertificateCreateWithData.
    public static func der(fromPEM pem: String) -> Data? {
        let lines = pem.split(whereSeparator: { $0 == "\n" || $0 == "\r" }).map { $0.trimmingCharacters(in: .whitespaces) }
        guard let start = lines.firstIndex(of: "-----BEGIN CERTIFICATE-----"),
              let end = lines[start...].firstIndex(of: "-----END CERTIFICATE-----"), end > start + 1 else { return nil }
        return Data(base64Encoded: lines[(start + 1)..<end].joined())
    }
}

/// Certificate fingerprints as node's fingerprint256 writes them: upper-case hex, colons between bytes.
public enum Fingerprint {
    public static func colonHex(_ bytes: [UInt8]) -> String {
        bytes.map { String(format: "%02X", $0) }.joined(separator: ":")
    }
}
