
import Foundation

// ---------------------------------------------------------------
// Argument handling
// ---------------------------------------------------------------
guard CommandLine.arguments.count >= 2 else {
    print("Usage: ThermalPrinterCLI <DEVICE_NAME> [MARKDOWN_FILE]")
    print("       ThermalPrinterCLI --list")
    exit(1)
}
let listMode = CommandLine.arguments[1] == "--list" || CommandLine.arguments[1] == "-l"

// ---------------------------------------------------------------
// List mode: scan and display all BLE peripherals, then exit.
// ---------------------------------------------------------------
if listMode {
    let printer = BLEPrinter(for: "any")
    do {
        let results = try printer.listPeripherals()
        if results.isEmpty {
            print("No BLE peripherals discovered.")
        } else {
            print("Discovered BLE peripherals:")
            for (name, identifier) in results {
                print("  \(name) (\(identifier))")
            }
        }
    } catch {
        print("❗️ Error listing peripherals: \(error.localizedDescription)")
        exit(1)
    }
    exit(0)
}

// ---------------------------------------------------------------
// Load markdown source (file or stdin)
// ---------------------------------------------------------------
let deviceName = CommandLine.arguments[1]
let inputPath = CommandLine.arguments.count >= 3 ? CommandLine.arguments[2] : nil

// ---------------------------------------------------------------
// Load markdown source (file or stdin)
// ---------------------------------------------------------------
let markdown: String
if let path = inputPath {
    do {
        markdown = try String(contentsOfFile: path, encoding: .utf8)
    } catch {
        print("❗️ Failed to read file '\(path)': \(error.localizedDescription)")
        exit(1)
    }
} else {
    var buffer = ""
    while let line = readLine() {
        buffer.append(line)
        buffer.append("\n")
    }
    markdown = buffer
}

// ---------------------------------------------------------------
// Convert to ESC/POS byte stream
// ---------------------------------------------------------------
let escData = convert(markdown: markdown)

// ---------------------------------------------------------------
// Send to printer
// ---------------------------------------------------------------
let printer = BLEPrinter(for: deviceName)
do {
    try printer.connect(to: deviceName)
    try printer.send(data: escData)
    printer.disconnect()
    print("✅ Print job sent to \(deviceName)")
} catch {
    print("❗️ Printer error: \(error.localizedDescription)")
    exit(1)
}
