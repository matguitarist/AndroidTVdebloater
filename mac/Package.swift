// swift-tools-version:5.10
// Native macOS app (SwiftUI). Build the .app with ./build-app.sh; `swift build` alone has no resources.
import PackageDescription

let package = Package(
    name: "AndroidTVDebloater",
    platforms: [.macOS(.v14)],
    targets: [
        .executableTarget(
            name: "AndroidTVDebloater",
            path: "Sources/AndroidTVDebloater"
        ),
    ]
)
