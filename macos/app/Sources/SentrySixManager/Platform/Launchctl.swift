import Foundation

/// A finished command: its exit status and what it printed.
struct CommandResult: Sendable {
    let status: Int32
    let stdout: String
    let stderr: String
}

/// Runs a program off the main thread and waits for it.
enum Command {
    static func run(_ path: String, _ args: [String]) async -> CommandResult {
        await Task.detached(priority: .userInitiated) {
            let p = Process()
            p.executableURL = URL(fileURLWithPath: path)
            p.arguments = args
            let out = Pipe()
            let err = Pipe()
            p.standardOutput = out
            p.standardError = err
            do {
                try p.run()
            } catch {
                return CommandResult(status: 127, stdout: "", stderr: error.localizedDescription)
            }
            // read both before waiting, so a full pipe cannot stall the child
            let o = out.fileHandleForReading.readDataToEndOfFile()
            let e = err.fileHandleForReading.readDataToEndOfFile()
            p.waitUntilExit()
            return CommandResult(status: p.terminationStatus, stdout: String(decoding: o, as: UTF8.self), stderr: String(decoding: e, as: UTF8.self))
        }.value
    }
}

/// launchctl for the jobs this app knows. Reading a job's state needs no admin.
enum Launchctl {
    static let path = "/bin/launchctl"

    static var userDomain: String { "gui/\(getuid())" }

    static func print(_ target: String) async -> LaunchdJob? {
        let r = await Command.run(path, ["print", target])
        return r.status == 0 ? LaunchdJob.parse(r.stdout) : nil
    }

    @discardableResult
    static func run(_ args: [String]) async -> CommandResult {
        await Command.run(path, args)
    }
}
