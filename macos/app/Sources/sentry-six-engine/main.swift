// sentry-six-engine: the "in your login" launcher (docs/plans/macos-app.md 3.3).
//
// The LaunchAgent starts this from inside the app bundle. A plist cannot
// expand ~, so this works out the user's paths, makes the folders, empties
// run/tmp, points stdout and stderr at the log (rotated here: newsyslog
// does not rotate a user's log), then execs node. Exec keeps the pid, so
// launchd still tracks the job.

import Foundation
import SentryKit
#if canImport(Darwin)
import Darwin
#elseif canImport(Glibc)
import Glibc
#endif

func die(_ message: String) -> Never {
    FileHandle.standardError.write(Data("sentry-six-engine: \(message)\n".utf8))
    // EX_CONFIG: launchd's KeepAlive starts it again after ThrottleInterval
    exit(78)
}

func homeFolder() -> String {
    // the account's home, never $HOME: launchd sets it, but nothing else checks it
    guard let pw = getpwuid(getuid()), let dir = pw.pointee.pw_dir else { die("cannot read this user's home folder") }
    return String(cString: dir)
}

func makeFolder(_ path: String, mode: Int) {
    var st = stat()
    if lstat(path, &st) == 0 {
        // a link or a file where a folder belongs: stop rather than follow it
        guard (st.st_mode & S_IFMT) == S_IFDIR else { die("\(path) is not a folder") }
        return
    }
    if mkdir(path, mode_t(mode)) != 0 && errno != EEXIST { die("cannot make \(path): \(String(cString: strerror(errno)))") }
    _ = chmod(path, mode_t(mode))
}

func emptyFolder(_ path: String) {
    let fm = FileManager.default
    for name in (try? fm.contentsOfDirectory(atPath: path)) ?? [] {
        // removeItem removes a link itself, never what it points to
        try? fm.removeItem(atPath: "\(path)/\(name)")
    }
}

func socketFolderUsable(_ dir: String, uid: UInt32) -> Bool {
    var st = stat()
    if lstat(dir, &st) != 0 {
        guard mkdir(dir, 0o700) == 0 else { return false }
        guard lstat(dir, &st) == 0 else { return false }
    }
    return SocketFolderCheck.acceptable(
        isDirectory: (st.st_mode & S_IFMT) == S_IFDIR, isSymlink: (st.st_mode & S_IFMT) == S_IFLNK,
        owner: UInt32(st.st_uid), mode: UInt16(st.st_mode & 0o7777), uid: uid)
}

let launcher = Bundle.main.executableURL?.resolvingSymlinksInPath().path ?? CommandLine.arguments[0]
guard let engine = AgentEnvironment.engineFolder(launcherPath: launcher) else {
    die("run me from Sentry Six Manager.app/Contents/MacOS, not \(launcher)")
}
let uid = UInt32(getuid())
let agent = AgentEnvironment(home: homeFolder(), engine: engine, uid: uid)
let paths = agent.paths

// 1. the folders: 0700, public/ 0755
let library = "\(paths.home)/Library"
for p in [library, "\(library)/Application Support", "\(library)/Logs"] { makeFolder(p, mode: 0o700) }
for f in paths.folders { makeFolder(f.path, mode: f.mode) }

// 2. run/tmp, the engine's TMPDIR, starts empty
emptyFolder(paths.tmp)

// 3. the log
LogRotation.rotate(log: paths.log)
let fd = open(paths.log, O_WRONLY | O_CREAT | O_APPEND, 0o600)
guard fd >= 0 else { die("cannot open \(paths.log)") }
dup2(fd, 1)
dup2(fd, 2)
close(fd)

// 4. the environment; the short socket folder only when it is really ours
var env = agent.environment
if agent.socketMoved && !socketFolderUsable(SocketPath.shortFallbackDir(uid: uid), uid: uid) {
    env.removeValue(forKey: "LOCAL_SIGNIN_SOCKET")
    FileHandle.standardError.write(Data("sentry-six-engine: \(SocketPath.shortFallbackDir(uid: uid)) is not this user's own folder; same-Mac sign-in is off\n".utf8))
}
let envp = env.sorted { $0.key < $1.key }.map { "\($0.key)=\($0.value)" }

// 5. node, in place of this process
let argv = agent.arguments
let cArgv = argv.map { strdup($0) } + [nil]
let cEnvp = envp.map { strdup($0) } + [nil]
execve(agent.node, cArgv, cEnvp)
die("cannot start \(agent.node): \(String(cString: strerror(errno)))")
