// swift-tools-version:6.0
// Sentry Six Manager for the Mac.
//
// SentryKit is the app's data layer: models, the API client, pinning rules,
// event streams, telemetry, playback decisions, view models and formatting.
// It builds and tests on Linux too. The app target (SwiftUI, AppKit,
// AVFoundation, MapKit) exists only when building on a Mac.

import PackageDescription

var products: [Product] = [
    .library(name: "SentryKit", targets: ["SentryKit"]),
]

var targets: [Target] = [
    .target(
        name: "SentryKit",
        path: "Sources/SentryKit"
    ),
    .testTarget(
        name: "SentryKitTests",
        dependencies: ["SentryKit"],
        path: "Tests/SentryKitTests",
        resources: [.copy("Fixtures")]
    ),
]

#if os(macOS)
products.append(.executable(name: "SentrySixManager", targets: ["SentrySixManager"]))
targets.append(
    .executableTarget(
        name: "SentrySixManager",
        dependencies: ["SentryKit"],
        path: "Sources/SentrySixManager"
    )
)
#endif

let package = Package(
    name: "SentrySixManager",
    platforms: [.macOS(.v14)],
    products: products,
    targets: targets,
    swiftLanguageModes: [.v6]
)
