import Foundation
import Security
import SentryKit

/// `Sentry Six Manager --check` (10.5 app-check): find the installed engine
/// through engine.json, pin it, ask /healthz and /api/auth/me, print one
/// plain line and exit 0 or 1. No window, no sign-in.
enum CheckCommand {
    static func runBlocking() -> Int32 {
        let done = DispatchSemaphore(value: 0)
        let box = ResultBox()
        Task.detached {
            box.code = await run()
            done.signal()
        }
        done.wait()
        return box.code
    }

    private final class ResultBox: @unchecked Sendable { var code: Int32 = 1 }

    static func run() async -> Int32 {
        let candidates = ServiceMode.allCases.map { EnginePaths(mode: $0, home: NSHomeDirectory()).engineJSON }
        guard let (path, info) = candidates.lazy.compactMap({ p in (try? Data(contentsOf: URL(fileURLWithPath: p))).flatMap(EngineInfo.decode).map { (p, $0) } }).first,
              let port = info.portNumber else {
            return say("No engine.json: Sentry Six Manager is not running here.", ok: false)
        }
        guard let tls = info.tls else { return say("\(path): HTTPS is turned off (TLS_MODE=off); the app does not sign in without it.", ok: false) }
        guard let policy = PinPolicy(tls) else { return say("\(path): its certificate cannot be pinned.", ok: false) }
        let pin = PinnedCheck(policy: policy)
        let session = URLSession(configuration: .ephemeral, delegate: pin, delegateQueue: nil)
        defer { session.invalidateAndCancel() }
        for p in ["/healthz", "/api/auth/me"] {
            guard let url = URL(string: "https://127.0.0.1:\(port)\(p)") else { return 1 }
            do {
                let (_, res) = try await session.data(for: URLRequest(url: url, timeoutInterval: 10))
                let status = (res as? HTTPURLResponse)?.statusCode ?? 0
                guard status == 200 else { return say("\(p) answered \(status).", ok: false) }
            } catch {
                return say("\(p) over pinned HTTPS failed: \(error.localizedDescription)", ok: false)
            }
        }
        return say("Sentry Six Manager \(info.version ?? "?") answers on port \(port), pinned (\(tls.mode ?? "?")).", ok: true)
    }

    private static func say(_ line: String, ok: Bool) -> Int32 {
        FileHandle.standardOutput.write(Data("\(line)\n".utf8))
        return ok ? 0 : 1
    }
}

/// The pin rule of 2.4 (SentryKit PinPolicy): local-ca trusts only the
/// pinned CA for 127.0.0.1; own trusts only the exact pinned leaf. Never
/// the system roots.
final class PinnedCheck: NSObject, URLSessionDelegate, @unchecked Sendable {
    private let policy: PinPolicy

    init(policy: PinPolicy) { self.policy = policy }

    func urlSession(_ session: URLSession, didReceive challenge: URLAuthenticationChallenge) async -> (URLSession.AuthChallengeDisposition, URLCredential?) {
        guard challenge.protectionSpace.authenticationMethod == NSURLAuthenticationMethodServerTrust,
              let trust = challenge.protectionSpace.serverTrust else { return (.performDefaultHandling, nil) }
        let chain = ((SecTrustCopyCertificateChain(trust) as? [SecCertificate]) ?? []).map { SecCertificateCopyData($0) as Data }
        var anchored = false
        if policy.mode == .localCA {
            guard let der = policy.caDER, let ca = SecCertificateCreateWithData(nil, der as CFData) else { return (.cancelAuthenticationChallenge, nil) }
            // the SSL policy for 127.0.0.1 also checks the leaf's dates and name
            SecTrustSetPolicies(trust, SecPolicyCreateSSL(true, "127.0.0.1" as CFString))
            SecTrustSetAnchorCertificates(trust, [ca] as CFArray)
            SecTrustSetAnchorCertificatesOnly(trust, true)
            anchored = SecTrustEvaluateWithError(trust, nil)
        }
        let presented = PinPolicy.Presented(chain: chain, anchoredEvaluationPassed: anchored, leafValidNow: anchored, leafNamesLoopback: anchored)
        return policy.evaluate(presented) == .accept ? (.useCredential, URLCredential(trust: trust)) : (.cancelAuthenticationChallenge, nil)
    }
}
