import Foundation

// Field names are exactly the server's. Every field is optional and numbers
// are Double, so a missing key, a null or a new key never fails a decode.

/// GET /api/auth/me
public struct AuthMe: Codable, Sendable, Equatable {
    public var user: String?
    public var needsSetup: Bool?
    public var secure: Bool?
    public var fingerprint: String?
    public var deployment: String?
}

/// GET /api/info
public struct ServerInfo: Codable, Sendable, Equatable {
    public var deployment: String?
}

/// {ok:true} and the like.
public struct OKAnswer: Codable, Sendable, Equatable {
    public var ok: Bool?
}

public struct LoginBody: Codable, Sendable { public var username: String; public var password: String }
public struct SetupBody: Codable, Sendable { public var username: String; public var password: String; public var code: String }
public struct ConfirmBody: Codable, Sendable { public var password: String }
public struct PasswordBody: Codable, Sendable { public var current: String; public var next: String }
/// POST /api/auth/local (same-computer sign-in, 2.6 of the contract)
public struct LocalSignInBody: Codable, Sendable { public var token: String }

/// POST /api/auth/local's answer.
public struct LocalSignInAnswer: Codable, Sendable, Equatable {
    public var ok: Bool?
    public var user: String?
    public var error: String?
    public var needsSetup: Bool?
}

/// The server's error envelope: {error, code?}, plus anything else it adds.
public struct ErrorEnvelope: Codable, Sendable, Equatable {
    public var error: String?
    public var code: String?
    public var needsSetup: Bool?
}
