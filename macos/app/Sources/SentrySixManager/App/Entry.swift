import Foundation

/// `--check` runs headless (CI's app-check job, 11.2); anything else opens the app.
@main
enum Entry {
    static func main() {
        if CommandLine.arguments.contains("--check") {
            exit(CheckCommand.runBlocking())
        }
        SentrySixManagerApp.main()
    }
}
