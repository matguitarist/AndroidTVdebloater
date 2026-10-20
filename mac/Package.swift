// swift-tools-version:5.10
// Native macOS app (SwiftUI). Build the .app with ./build-app.sh; `swift build` alone has no resources.
import PackageDescription

let package = Package(
    name: "AndroidTVDebloater",
    platforms: [.macOS(.v14)],
    dependencies: [
        // Updates: appcast + EdDSA-signed zip on GitHub Releases.
        .package(url: "https://github.com/sparkle-project/Sparkle", exact: "2.10.0"),
    ],
    targets: [
        .executableTarget(
            name: "AndroidTVDebloater",
            dependencies: [.product(name: "Sparkle", package: "Sparkle")],
            path: "Sources/AndroidTVDebloater"
        ),
    ]
)
