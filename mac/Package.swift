// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "PhoneDrop",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(
            name: "PhoneDrop",
            path: "Sources/PhoneDrop"
        ),
    ]
)
