import Foundation

/// Which TLS server the app accepts (2.4). The app's PinnedTrust runs the
/// Security framework checks and hands their results here.
public struct PinPolicy: Sendable, Equatable {
    public enum Mode: Sendable, Equatable { case localCA, own }

    public var mode: Mode
    /// the CA's DER (local-ca)
    public var caDER: Data?
    public var caFingerprint: String?
    /// the leaf's fingerprint (own)
    public var leafFingerprint: String?

    public init?(_ tls: EngineTLS) {
        switch tls.mode {
        case "local-ca":
            guard let pem = tls.caPem, let der = PEM.certificates(pem).first, let fp = tls.caSha256 else { return nil }
            // the file's own fingerprint must match its certificate
            guard SHA256.fingerprint(der) == fp.uppercased() else { return nil }
            mode = .localCA; caDER = der; caFingerprint = fp.uppercased(); leafFingerprint = nil
        case "own":
            guard let fp = tls.leafSha256 else { return nil }
            mode = .own; caDER = nil; caFingerprint = nil; leafFingerprint = fp.uppercased()
        default:
            return nil
        }
    }

    /// What the Security framework found for the presented chain.
    public struct Presented: Sendable, Equatable {
        /// leaf first, as DER
        public var chain: [Data]
        /// SecTrustEvaluateWithError with the pinned CA as the only anchor
        public var anchoredEvaluationPassed: Bool
        /// the leaf is valid now
        public var leafValidNow: Bool
        /// the leaf names 127.0.0.1 (the SSL policy for that host passed)
        public var leafNamesLoopback: Bool

        public init(chain: [Data], anchoredEvaluationPassed: Bool, leafValidNow: Bool, leafNamesLoopback: Bool) {
            self.chain = chain; self.anchoredEvaluationPassed = anchoredEvaluationPassed
            self.leafValidNow = leafValidNow; self.leafNamesLoopback = leafNamesLoopback
        }

        public var leafFingerprint: String? { chain.first.map(SHA256.fingerprint) }
    }

    public enum Decision: Sendable, Equatable {
        case accept
        case reject(reason: String, expected: String, presented: String)
    }

    public var expectedFingerprint: String { (mode == .localCA ? caFingerprint : leafFingerprint) ?? "" }

    public func evaluate(_ p: Presented) -> Decision {
        let presented: String
        switch mode {
        case .localCA:
            // the chain's top certificate when it has one, else the leaf
            presented = (p.chain.count > 1 ? p.chain.last.map(SHA256.fingerprint) : p.leafFingerprint) ?? "none"
            if p.chain.isEmpty { return .reject(reason: "no certificate", expected: expectedFingerprint, presented: presented) }
            if !p.anchoredEvaluationPassed { return .reject(reason: "not signed by the pinned CA", expected: expectedFingerprint, presented: presented) }
            if !p.leafValidNow { return .reject(reason: "the certificate is not valid now", expected: expectedFingerprint, presented: presented) }
            if !p.leafNamesLoopback { return .reject(reason: "the certificate does not name 127.0.0.1", expected: expectedFingerprint, presented: presented) }
            return .accept
        case .own:
            presented = p.leafFingerprint ?? "none"
            return presented == leafFingerprint ? .accept : .reject(reason: "not the pinned certificate", expected: expectedFingerprint, presented: presented)
        }
    }
}

/// After a rejected handshake: re-read engine.json once (the CA may just
/// have been replaced), then show the pinning screen.
public struct PinRetry: Sendable, Equatable {
    public enum Step: Sendable, Equatable { case rereadAndRetry, showPinningScreen }
    private var retried = false

    public init() {}

    public mutating func rejected() -> Step {
        if retried { return .showPinningScreen }
        retried = true
        return .rereadAndRetry
    }

    public mutating func accepted() { retried = false }
}
