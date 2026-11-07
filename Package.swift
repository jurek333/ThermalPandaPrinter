// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "ThermalPrinterCLI",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "ThermalPrinterCLI",
                    targets: ["ThermalPrinterCLI"]),
    ],
    targets: [
        .executableTarget(
            name: "ThermalPrinterCLI",
            dependencies: [],
            path: "ThermalPrinterCLI",
            linkerSettings: [
                .linkedFramework("CoreBluetooth")
            ]
        )
    ]
)
