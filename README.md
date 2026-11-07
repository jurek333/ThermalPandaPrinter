# ThermalPrinterCLI

A tiny macOS command‑line tool that sends **markdown‑style** text to a Bluetooth thermal printer (ESC/POS).

## Prerequisites
* macOS 13+ (Apple Silicon or Intel)
* Xcode command‑line tools (`xcode-select --install`)
* The printer must be paired in **System Settings ▸ Bluetooth** – note its MAC address (e.g. `AA-BB-CC-DD-EE-FF`).

## Build

```bash
cd ThermalPrinterCLI
swift build -c release          # produces .build/release/ThermalPrinterCLI
```

## Run

```bash
# print a file
./.build/release/ThermalPrinterCLI AA-BB-CC-DD-EE-FF path/to/file.md

# or pipe stdin
cat mynote.md | ./.build/release/ThermalPrinterCLI AA-BB-CC-DD-EE-FF
```

Supported markdown‑like syntax

| Syntax | Result |
|-------|--------|
| `# Heading` | double‑height/width |
| `**bold**` | ESC `E 1` … ESC `E 0` |
| `*italic*` | ESC `- 1` … ESC `- 0` (underlined) |
| plain text | printed as‑is |

If your printer uses BLE instead of classic SPP, replace `BluetoothPrinter` with a CoreBluetooth implementation – the markdown→ESC/POS pipeline stays unchanged.

## ThermalMusicPrinter

Separate .NET 10 CLI for music notation that prints to MX/MXW01 thermal printers. alphaTab reads Guitar Pro, MusicXML, Capella, and structured alphaTex files; guitar scores render standard notation and tablature together. Dedicated harmonica-tab commands render explicit hole/breath notation; plain prose is not transcribed automatically.

### Build

```bash
/usr/local/share/dotnet/dotnet build ThermalMusicPrinter/ThermalMusicPrinter.csproj
```

### Use

```bash
# Preview the included guitar score
/usr/local/share/dotnet/dotnet run --project ThermalMusicPrinter/ThermalMusicPrinter.csproj -- render ThermalMusicPrinter/Examples/melody.alphatex --output /tmp/score.png

# Compare the included short song in the two roll layouts
/usr/local/share/dotnet/dotnet run --project ThermalMusicPrinter/ThermalMusicPrinter.csproj -- render ThermalMusicPrinter/Examples/mary-had-a-little-lamb.alphatex --output /tmp/mary-one.png --rotate 1
/usr/local/share/dotnet/dotnet run --project ThermalMusicPrinter/ThermalMusicPrinter.csproj -- render ThermalMusicPrinter/Examples/mary-had-a-little-lamb.alphatex --output /tmp/mary-two.png --rotate 2

# Create a raw one-bit MXW01 raster without connecting to a printer
/usr/local/share/dotnet/dotnet run --project ThermalMusicPrinter/ThermalMusicPrinter.csproj -- raster path/to/score.gp --output /tmp/score.bin

# Discover BLE devices, then print by device name
/usr/local/share/dotnet/dotnet run --project ThermalMusicPrinter/ThermalMusicPrinter.csproj -- devices
/usr/local/share/dotnet/dotnet run --project ThermalMusicPrinter/ThermalMusicPrinter.csproj -- print "MXW01" path/to/score.musicxml --rotate 1
/usr/local/share/dotnet/dotnet run --project ThermalMusicPrinter/ThermalMusicPrinter.csproj -- print "MXW01" path/to/score.musicxml --rotate 2
# Preview and print the included Basho haiku in Japanese
/usr/local/share/dotnet/dotnet run --project ThermalMusicPrinter/ThermalMusicPrinter.csproj -- text ThermalMusicPrinter/Examples/basho-haiku.txt --output /tmp/haiku.png
/usr/local/share/dotnet/dotnet run --project ThermalMusicPrinter/ThermalMusicPrinter.csproj -- print-text "MXW01" ThermalMusicPrinter/Examples/basho-haiku.txt
# Preview a harmonica tab file
/usr/local/share/dotnet/dotnet run --project ThermalMusicPrinter/ThermalMusicPrinter.csproj -- harmonica path/to/song.harmtab --output /tmp/harmonica.png

# Print harmonica tabs to an MXW01
/usr/local/share/dotnet/dotnet run --project ThermalMusicPrinter/ThermalMusicPrinter.csproj -- print-harmonica "MXW01" path/to/song.harmtab
```

MXW01 printing requires a 384-pixel-wide raster and uses GATT service `AE30` (control `AE01`, notifications `AE02`, image data `AE03`). The sender checks printer status, transfers the 1-bit raster, verifies its completion checksum, and feeds the paper. A bundled SwiftPM helper uses macOS CoreBluetooth; `/usr/bin/swift` and Xcode command-line tools are required for `devices`, `print`, `print-text`, and `print-harmonica`.

`--rotate 1` prints standard notation only, with musical time running down the roll. `--rotate 2` prints standard notation and guitar tablature as adjacent staff rows across the paper width. The app lays out one long horizontal score, rotates it, and scales the staff rows to the 384-dot width.

`text` and `print-text` read UTF-8 and rasterize glyphs using installed macOS Japanese fonts (Hiragino Mincho ProN, with fallbacks). The existing Swift Markdown CLI writes UTF-8 bytes directly; it does not rasterize Japanese glyphs and uses a different BLE printer protocol. Use `ThermalMusicPrinter print-text` for the MXW01.

`harmonica`, `raster-harmonica`, and `print-harmonica` use a dedicated UTF-8 tab renderer. Put an optional title on a `# Title` line; separate notes with spaces and measures with `|`. A hole number (`1`–`10`) means blow, a leading minus means draw, and each trailing apostrophe marks one half-step bend (for example, `-3'` and `-3''`). Adjacent digits such as `12` mean play those holes together. `print-harmonica` sends the 384-pixel-wide raster to the MXW01.
