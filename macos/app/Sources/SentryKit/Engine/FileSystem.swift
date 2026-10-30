import Foundation
#if canImport(Glibc)
import Glibc
#elseif canImport(Darwin)
import Darwin
#endif

/// What lstat says about a path: enough for the ownership checks of 2.3 and 2.6.
public struct FileStatus: Sendable, Equatable {
    public enum Kind: Sendable, Equatable { case file, directory, symlink, socket, other }
    public var kind: Kind
    public var ownerUID: UInt32
    /// the permission bits, e.g. 0o755
    public var mode: UInt16

    public init(kind: Kind, ownerUID: UInt32, mode: UInt16) { self.kind = kind; self.ownerUID = ownerUID; self.mode = mode }

    public var writableByOthers: Bool { mode & 0o022 != 0 }
}

/// The file checks behind a protocol, so they test on Linux with a fake.
public protocol FileChecking: Sendable {
    /// lstat: never follows a symlink; nil when the path does not exist
    func status(_ path: String) -> FileStatus?
    func read(_ path: String) -> Data?
    var currentUID: UInt32 { get }
    /// getpwnam(name).pw_uid
    func uid(ofUser name: String) -> UInt32?
}

/// The real file system.
public struct PosixFileSystem: FileChecking {
    public init() {}

    public func status(_ path: String) -> FileStatus? {
        var st = stat()
        guard lstat(path, &st) == 0 else { return nil }
        let fmt = UInt32(st.st_mode) & UInt32(S_IFMT)
        let kind: FileStatus.Kind
        switch fmt {
        case UInt32(S_IFREG): kind = .file
        case UInt32(S_IFDIR): kind = .directory
        case UInt32(S_IFLNK): kind = .symlink
        case UInt32(S_IFSOCK): kind = .socket
        default: kind = .other
        }
        return FileStatus(kind: kind, ownerUID: UInt32(st.st_uid), mode: UInt16(UInt32(st.st_mode) & 0o7777))
    }

    public func read(_ path: String) -> Data? { FileManager.default.contents(atPath: path) }
    public var currentUID: UInt32 { UInt32(getuid()) }

    public func uid(ofUser name: String) -> UInt32? {
        guard let pw = getpwnam(name) else { return nil }
        return UInt32(pw.pointee.pw_uid)
    }
}
