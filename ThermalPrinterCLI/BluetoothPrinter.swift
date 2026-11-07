
import CoreBluetooth

// MARK: - Bluetooth Printer for BLE
final class BLEPrinter: NSObject, CBCentralManagerDelegate, CBPeripheralDelegate {
    private var central: CBCentralManager!
    private var peripheral: CBPeripheral? 
    private var characteristic: CBCharacteristic?

    // Discovery state tracking
    private let targetName: String
    private let discoveredPeripheralUUID: CBUUID = CBUUID(string: "FFE0")
    private let writeCharacteristicUUID: CBUUID = CBUUID(string: "FFE1")
    private let discoverySemaphore = DispatchSemaphore(value: 0)
    private let connectionSemaphore = DispatchSemaphore(value: 0)

    // List mode state
    private var discoveredPeripherals: [(name: String, identifier: String)] = []

    init(for deviceName: String) {
        self.targetName = deviceName
        super.init()
        central = CBCentralManager(delegate: self, queue: nil)
    }

    // MARK: - Public API

    /// Blocks until the printer is connected and its write characteristic is discovered.
    /// Returns `true` on success, `false` on timeout or failure.
    @discardableResult func connect(to deviceName: String) -> Bool {
        // Scan for ALL peripherals (no service filter).
        central.scanForPeripherals(withServices: nil, options: nil)

        if discoverySemaphore.wait(timeout: .now() + 10) == .timedOut {
            print("❗️ No peripherals discovered in time.")
            return false
        }

        guard let peripheral = peripheral else {
            print("❗️ No matching peripheral found.")
            return false
        }

        central.connect(peripheral, options: nil)
        if connectionSemaphore.wait(timeout: .now() + 10) == .timedOut {
            print("❗️ Connection timeout.")
            return false
        }

        // Wait for the writable characteristic to be discovered.
        Thread.sleep(forTimeInterval: 0.5)

        guard characteristic != nil else {
            print("❗️ No write characteristic found on printer.")
            return false
        }

        print("✅ Connected to \(peripheral.name ?? "Unknown") — characteristic ready.")
        return true
    }

    /// Scans for BLE peripherals and returns all discovered ones.
    /// Blocks until the scan timeout expires. Returns an array of (name, identifier) tuples.
    func listPeripherals() throws -> [(name: String, identifier: String)] {
        discoveredPeripherals = []
        // Scan for ALL peripherals (no service filter).
        central.scanForPeripherals(withServices: nil, options: nil)

        // Wait for scan to complete (10 second timeout).
        Thread.sleep(forTimeInterval: 10)
        central.stopScan()

        return discoveredPeripherals
    }

    /// Sends ESC/POS data to the printer. Returns `true` if all chunks were accepted.
    @discardableResult func send(data: Data) -> Bool {
        guard let characteristic = characteristic else {
            print("❗️ No characteristic found to write to.")
            return false
        }

        var offset = 0
        while offset < data.count {
            let chunkSize = min(20, data.count - offset)
            let chunk = data.subdata(in: offset ..< offset + chunkSize)

            peripheral?.writeValue(chunk, for: characteristic, type: .withResponse)
            offset += chunkSize

            // Brief pause between chunks to avoid overwhelming the printer.
            Thread.sleep(forTimeInterval: 0.01)
        }

        return true
    }

    func disconnect() {
        guard let peripheral = peripheral else { return }
        central.cancelPeripheralConnection(peripheral)
    }

    // MARK: - Central Manager Delegate

    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        if central.state == .poweredOn {
            print("Bluetooth is powered on.")
        } else {
            print("❗️ Bluetooth not available (state: \(central.state)).")
        }
    }

    func centralManager(_ central: CBCentralManager, didDiscover peripheral: CBPeripheral, advertisementData: [String : Any], rssi RSSI: NSNumber) {
        let peripheralName = advertisementData[CBAdvertisementDataLocalNameKey] as? String ?? ""

        // Always record the discovery for list mode.
        discoveredPeripherals.append((name: peripheral.name ?? "Unknown", identifier: peripheral.identifier.uuidString))

        // In connect mode, only act on the target name.
        if peripheralName != targetName {
            print("🔍 Discovered \(peripheral.name ?? "Unknown") (\(peripheral.identifier.uuidString)), skipping.")
            return
        }

        print("🎯 Discovered target printer: \(peripheral.name ?? "Unknown") (\(peripheral.identifier.uuidString))")
        self.peripheral = peripheral
        discoverySemaphore.signal()
        central.stopScan()
    }

    func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        print("Connected to \(peripheral.name ?? "Unknown")")
        peripheral.delegate = self
        peripheral.discoverServices([discoveredPeripheralUUID])
        connectionSemaphore.signal()
    }

    func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
        if let services = peripheral.services {
            for service in services {
                if service.uuid == discoveredPeripheralUUID {
                    print("Found service \(service.uuid); discovering characteristics.")
                    peripheral.discoverCharacteristics([writeCharacteristicUUID], for: service)
                }
            }
        } else if let error = error {
            print("❗️ Error discovering services: \(error.localizedDescription)")
        }
    }

    func peripheral(_ peripheral: CBPeripheral, didDiscoverCharacteristicsFor service: CBService, error: Error?) {
        if let characteristics = service.characteristics {
            for char in characteristics {
                if char.uuid == writeCharacteristicUUID {
                    self.characteristic = char
                    print("Found writable characteristic: \(char.uuid)")
                }
            }
        } else if let error = error {
            print("❗️ Error discovering characteristics: \(error.localizedDescription)")
        }
    }

    func peripheral(_ peripheral: CBPeripheral, didWriteValueFor characteristic: CBCharacteristic, error: Error?) {
        if let error = error {
            print("❗️ Write error: \(error.localizedDescription)")
        } else {
            print("✅ Chunk written to characteristic \(characteristic.uuid).")
        }
    }

}
