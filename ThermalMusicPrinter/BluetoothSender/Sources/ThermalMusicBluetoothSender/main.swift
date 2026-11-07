import CoreBluetooth
import Darwin
import Foundation

@main
struct ThermalMusicBluetoothSender {
    static func main() {
        let arguments = Array(CommandLine.arguments.dropFirst())

        if arguments.count == 1, arguments[0] == "devices" {
            let sender = BluetoothSender(mode: .list)
            sender.start()
            RunLoop.main.run()
            return
        }

        guard arguments.count == 3, arguments[0] == "send" else {
            fail("Usage: ThermalMusicBluetoothSender devices | send <device-name> <raster-file>")
        }

        do {
            let raster = try Data(contentsOf: URL(fileURLWithPath: arguments[2]))
            guard !raster.isEmpty else { fail("Monochrome raster is empty") }
            guard raster.count % 48 == 0 else { fail("MXW01 raster must contain 48 bytes per 384-pixel row") }
            guard raster.count / 48 <= Int(UInt16.max) else { fail("MXW01 print height exceeds 65535 rows") }

            let sender = BluetoothSender(mode: .print(deviceName: arguments[1], raster: raster))
            sender.start()
            RunLoop.main.run()
        } catch {
            fail(error.localizedDescription)
        }
    }
}

private enum SenderMode {
    case list
    case print(deviceName: String, raster: Data)
}

private enum PrintStage {
    case idle
    case waitingForStatus
    case waitingForPrintAck
    case sendingRaster
    case flushingRaster
    case waitingForPrintDone
    case waitingForFeedAck
}

private final class BluetoothSender: NSObject, CBCentralManagerDelegate, CBPeripheralDelegate {
    private static let serviceId = CBUUID(string: "0000AE30-0000-1000-8000-00805F9B34FB")
    private static let controlId = CBUUID(string: "0000AE01-0000-1000-8000-00805F9B34FB")
    private static let notifyId = CBUUID(string: "0000AE02-0000-1000-8000-00805F9B34FB")
    private static let dataId = CBUUID(string: "0000AE03-0000-1000-8000-00805F9B34FB")

    private let mode: SenderMode
    private var central: CBCentralManager!
    private var peripheral: CBPeripheral?
    private var control: CBCharacteristic?
    private var notify: CBCharacteristic?
    private var data: CBCharacteristic?
    private var foundDevices: [String: String] = [:]
    private var raster = Data()
    private var rasterOffset = 0
    private var lineCount = 0
    private var imageCrc: UInt8 = 0
    private var stage = PrintStage.idle
    private var rasterPumpScheduled = false
    private var timer: DispatchWorkItem?
    private var finished = false
    private var paceMilliseconds = 8
    private var writeStalls = 0

    init(mode: SenderMode) {
        self.mode = mode
        if case let .print(_, image) = mode {
            raster = image
            lineCount = image.count / 48
        }
        super.init()
        central = CBCentralManager(delegate: self, queue: .main)
    }

    func start() {
        let timeout: TimeInterval
        if case .list = mode {
            timeout = 8
        } else {
            timeout = max(120, min(900, 45 + Double(lineCount) * 0.1))
        }
        armTimeout(timeout, message: "Timed out while scanning, connecting, or printing")
    }

    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        switch central.state {
        case .poweredOn:
            // MXW01 advertises AF30 but exposes AE30 after connection; scan unfiltered.
            central.scanForPeripherals(withServices: nil, options: [CBCentralManagerScanOptionAllowDuplicatesKey: false])
        case .unknown, .resetting:
            break
        case .poweredOff:
            fail("Bluetooth is powered off")
        case .unauthorized:
            fail("Bluetooth access is not authorized for this app")
        case .unsupported:
            fail("This Mac does not support Bluetooth LE")
        @unknown default:
            fail("Bluetooth entered an unknown state")
        }
    }

    func centralManager(
        _ central: CBCentralManager,
        didDiscover peripheral: CBPeripheral,
        advertisementData: [String: Any],
        rssi RSSI: NSNumber
    ) {
        let name = (advertisementData[CBAdvertisementDataLocalNameKey] as? String) ?? peripheral.name ?? ""
        guard !name.isEmpty else { return }
        foundDevices[peripheral.identifier.uuidString] = name

        guard case let .print(deviceName, _)=mode,
              name.localizedCaseInsensitiveCompare(deviceName) == .orderedSame,
              self.peripheral == nil else { return }

        self.peripheral = peripheral
        central.stopScan()
        central.connect(peripheral, options: nil)
    }

    func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        self.peripheral = peripheral
        peripheral.delegate = self
        peripheral.discoverServices([Self.serviceId])
    }

    func centralManager(_ central: CBCentralManager, didFailToConnect peripheral: CBPeripheral, error: Error?) {
        fail("Could not connect to \(peripheral.name ?? "the printer"): \(error?.localizedDescription ?? "connection failed")")
    }

    func centralManager(_ central: CBCentralManager, didDisconnectPeripheral peripheral: CBPeripheral, error: Error?) {
        guard !finished else { return }
        fail("Printer disconnected before print completion: \(error?.localizedDescription ?? "connection closed")")
    }

    func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
        if let error {
            fail("Could not discover printer services: \(error.localizedDescription)")
            return
        }
        guard let service = peripheral.services?.first(where: { $0.uuid == Self.serviceId }) else {
            fail("MXW01 GATT service AE30 was not found")
            return
        }
        peripheral.discoverCharacteristics([Self.controlId, Self.notifyId, Self.dataId], for: service)
    }

    func peripheral(_ peripheral: CBPeripheral, didDiscoverCharacteristicsFor service: CBService, error: Error?) {
        if let error {
            fail("Could not discover MXW01 characteristics: \(error.localizedDescription)")
            return
        }
        let characteristics = service.characteristics ?? []
        control = characteristics.first(where: { $0.uuid == Self.controlId })
        notify = characteristics.first(where: { $0.uuid == Self.notifyId })
        data = characteristics.first(where: { $0.uuid == Self.dataId })

        guard let control, control.properties.contains(.writeWithoutResponse),
              let notify, notify.properties.contains(.notify) || notify.properties.contains(.indicate),
              let data, data.properties.contains(.writeWithoutResponse) else {
            fail("MXW01 service AE30 is missing writable AE01/AE03 or notify AE02")
            return
        }
        peripheral.setNotifyValue(true, for: notify)
    }

    func peripheral(_ peripheral: CBPeripheral, didUpdateNotificationStateFor characteristic: CBCharacteristic, error: Error?) {
        guard characteristic.uuid == Self.notifyId else { return }
        if let error {
            fail("Could not enable MXW01 notifications: \(error.localizedDescription)")
            return
        }
        guard characteristic.isNotifying else {
            fail("MXW01 notifications are not enabled")
            return
        }
        beginPrintSequence()
    }

    func peripheral(_ peripheral: CBPeripheral, didUpdateValueFor characteristic: CBCharacteristic, error: Error?) {
        guard characteristic.uuid == Self.notifyId, !finished else { return }
        if let error {
            fail("MXW01 notification failed: \(error.localizedDescription)")
            return
        }
        guard let packet = Self.parseNotification(characteristic.value) else { return }

        switch (stage, packet.command) {
        case (.waitingForStatus, 0xA1):
            handleStatus(packet.payload)
        case (.waitingForPrintAck, 0xA9):
            handlePrintAcknowledgement(packet.payload)
        case (.waitingForPrintDone, 0xAA):
            handlePrintCompletion(packet.payload)
        case (.waitingForFeedAck, 0xA3):
            finishSuccess()
        default:
            break
        }
    }

    func peripheralIsReady(toSendWriteWithoutResponse peripheral: CBPeripheral) {
        guard stage == .sendingRaster else { return }
        sendNextRasterChunk()
    }

    private func beginPrintSequence() {
        stage = .idle
        sendCommand(0xA2, payload: [0x5D]) // Conservative, verified print intensity.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) { [weak self] in
            self?.requestStatus()
        }
    }

    private func requestStatus() {
        guard !finished else { return }
        stage = .waitingForStatus
        armTimeout(4, message: "MXW01 did not answer the status request")
        sendCommand(0xA1, payload: [0x00])
    }

    private func handleStatus(_ payload: Data) {
        cancelTimeout()
        guard payload.count >= 7 else {
            fail("MXW01 returned a truncated status response")
            return
        }
        if payload.count > 9 && payload[9] != 0 {
            fail("MXW01 reports no paper (status byte 9: 0x\(String(payload[9], radix: 16)))")
            return
        }
        if payload[6] != 0 {
            fail("MXW01 reports error flag 0x\(String(payload[6], radix: 16))")
            return
        }
        if payload.count > 4 && payload[4] > 38 {
            fail("MXW01 print head is \(payload[4])°C; wait for it to cool below 39°C")
            return
        }

        stage = .waitingForPrintAck
        armTimeout(4, message: "MXW01 did not acknowledge the print request")
        sendCommand(0xA9, payload: [UInt8(lineCount & 0xFF), UInt8((lineCount >> 8) & 0xFF), 0x30, 0x01])
    }

    private func handlePrintAcknowledgement(_ payload: Data) {
        cancelTimeout()
        guard let status = payload.first else {
            fail("MXW01 returned an empty print acknowledgement")
            return
        }
        guard status == 0 else {
            fail("MXW01 rejected the print request (status 0x\(String(status, radix: 16)))")
            return
        }
        stage = .sendingRaster
        sendNextRasterChunk()
    }

    private func sendNextRasterChunk() {
        guard !rasterPumpScheduled, !finished, stage == .sendingRaster,
              let peripheral, let data else { return }
        guard rasterOffset < raster.count else {
            stage = .flushingRaster
            scheduleRasterPump(after: 0.1) { [weak self] in self?.flushRaster() }
            return
        }
        guard peripheral.canSendWriteWithoutResponse else {
            writeStalls += 1
            paceMilliseconds = min(paceMilliseconds + 2, 40)
            scheduleRasterPump(after: 0.01) { [weak self] in self?.sendNextRasterChunk() }
            return
        }

        let maximum = max(1, min(512, peripheral.maximumWriteValueLength(for: .withoutResponse)))
        let length = min(48, maximum, raster.count - rasterOffset)
        let end = rasterOffset + length
        let chunk = raster.subdata(in: rasterOffset..<end)
        peripheral.writeValue(chunk, for: data, type: .withoutResponse)
        imageCrc = Self.crc8(chunk, initial: imageCrc)
        rasterOffset = end

        scheduleRasterPump(after: Double(paceMilliseconds) / 1000) { [weak self] in
            self?.sendNextRasterChunk()
        }
    }

    private func scheduleRasterPump(after delay: TimeInterval, _ action: @escaping () -> Void) {
        guard !rasterPumpScheduled else { return }
        rasterPumpScheduled = true
        DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in
            guard let self else { return }
            self.rasterPumpScheduled = false
            action()
        }
    }

    private func flushRaster() {
        guard !finished, stage == .flushingRaster else { return }
        stage = .waitingForPrintDone
        armTimeout(8 + Double(lineCount) * 0.07, message: "MXW01 did not report print completion")
        sendCommand(0xAD, payload: [0x00])
    }
    private func handlePrintCompletion(_ payload: Data) {
        cancelTimeout()
        guard payload.count > 1 else {
            fail("MXW01 completion notification omitted the raster checksum")
            return
        }
        guard payload[1] == imageCrc else {
            fail("MXW01 raster checksum mismatch (sent 0x\(String(imageCrc, radix: 16)), received 0x\(String(payload[1], radix: 16)))")
            return
        }

        stage = .waitingForFeedAck
        armTimeout(4, message: "MXW01 printed but did not acknowledge paper feed")
        sendCommand(0xA3, payload: [0x07, 0x00])
    }

    private func sendCommand(_ command: UInt8, payload: [UInt8]) {
        guard let peripheral, let control else {
            fail("MXW01 control characteristic AE01 is unavailable")
            return
        }
        peripheral.writeValue(Self.frame(command, payload: payload), for: control, type: .withoutResponse)
    }

    private func armTimeout(_ seconds: TimeInterval, message: String) {
        cancelTimeout()
        let work = DispatchWorkItem { [weak self] in self?.fail(message) }
        timer = work
        DispatchQueue.main.asyncAfter(deadline: .now() + seconds, execute: work)
    }

    private func cancelTimeout() {
        timer?.cancel()
        timer = nil
    }

    private func finishSuccess() {
        guard !finished else { return }
        finished = true
        cancelTimeout()
        central.stopScan()
        if let peripheral {
            central.cancelPeripheralConnection(peripheral)
        }
        writeOutput("MXW01 printed \(lineCount) rows; image checksum 0x\(String(imageCrc, radix: 16)); paced \(paceMilliseconds) ms/row (\(writeStalls) stalls).\n")
        exit(0)
    }

    private func printDevicesAndExit() {
        guard !finished else { return }
        finished = true
        cancelTimeout()
        central.stopScan()
        if foundDevices.isEmpty {
            writeOutput("No BLE devices discovered.\n")
        } else {
            for (identifier, name) in foundDevices.sorted(by: { $0.value.localizedCaseInsensitiveCompare($1.value) == .orderedAscending }) {
                writeOutput("\(name) (\(identifier))\n")
            }
        }
        exit(0)
    }

    private func fail(_ message: String) {
        guard !finished else { return }
        finished = true
        cancelTimeout()
        central.stopScan()
        if let peripheral {
            central.cancelPeripheralConnection(peripheral)
        }
        writeError("Error: \(message)\n")
        exit(1)
    }

    private static func frame(_ command: UInt8, payload: [UInt8]) -> Data {
        var bytes: [UInt8] = [0x22, 0x21, command, 0x00, UInt8(payload.count & 0xFF), UInt8((payload.count >> 8) & 0xFF)]
        bytes.append(contentsOf: payload)
        bytes.append(crc8(Data(payload)))
        bytes.append(0xFF)
        return Data(bytes)
    }

    private static func parseNotification(_ data: Data?) -> (command: UInt8, payload: Data)? {
        guard let data, data.count >= 7,
              data[0] == 0x22, data[1] == 0x21 else { return nil }
        let length = Int(data[4]) | (Int(data[5]) << 8)
        guard data.count >= 6 + length else { return nil }
        return (data[2], data.subdata(in: 6..<(6 + length)))
    }

    private static func crc8(_ data: Data, initial: UInt8 = 0) -> UInt8 {
        var crc = initial
        for byte in data {
            crc ^= byte
            for _ in 0..<8 {
                crc = (crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1
            }
        }
        return crc
    }
}

private func writeOutput(_ value: String) {
    FileHandle.standardOutput.write(Data(value.utf8))
}

private func writeError(_ value: String) {
    FileHandle.standardError.write(Data(value.utf8))
}

private func fail(_ message: String) -> Never {
    writeError("Error: \(message)\n")
    exit(1)
}
