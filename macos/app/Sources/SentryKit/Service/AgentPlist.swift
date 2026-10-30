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
