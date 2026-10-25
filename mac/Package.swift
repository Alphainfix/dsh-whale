// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "DSHWhale",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(
            name: "DSHWhale",
            path: "Sources/DSHWhale",
            resources: [
                .copy("Resources/menubar.png"),
                .copy("Resources/menubar@2x.png"),
                .copy("Resources/whale-glyph.png"),
                .copy("Resources/whale-glyph@2x.png")
            ]
        )
    ]
)
