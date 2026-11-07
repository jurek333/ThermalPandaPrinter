// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "ThermalMusicBluetoothSender",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "ThermalMusicBluetoothSender", targets: ["ThermalMusicBluetoothSender"])
    ],
    targets: [
        .executableTarget(name: "ThermalMusicBluetoothSender")
    ]
)
