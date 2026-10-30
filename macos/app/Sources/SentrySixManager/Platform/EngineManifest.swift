// The SHA-256 of the bundled engine's MANIFEST.sha256, compiled into the app
// (docs/plans/macos-app.md 3.4). scripts/build-app.sh writes
// EngineManifest.generated.swift and builds with -DENGINE_MANIFEST; a plain
// `swift build` has none, and then the background service cannot be installed.

enum EngineManifest {
    #if ENGINE_MANIFEST
    static let sha256: String? = generatedEngineManifestSHA256
    #else
    static let sha256: String? = nil
    #endif
}
