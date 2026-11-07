
namespace ThermalMusicPrinter;

internal static class Program
{
    private const int DefaultWidth = 384;
    private const int MinimumWidth = 128;
    private const int MaximumWidth = 1024;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                PrintUsage();
                return args.Length == 0 ? 2 : 0;
            }

            switch (args[0])
            {
                case "devices":
                    if (args.Length != 1)
                    {
                        throw new ArgumentException("The devices command takes no options.");
                    }
                    return await BluetoothBridge.RunAsync(["devices"]);

                case "render":
                {
                    var options = ParseOptions(args, positionalCount: 1, requireOutput: true);
                    var rendered = MusicScoreRenderer.Render(options.Positionals[0], options.Width, options.RotateRows);
                    WriteFile(options.Output!, rendered.Png);
                    Console.WriteLine($"Rendered {rendered.Width}×{rendered.Height} PNG to {options.Output}");
                    return 0;
                }

                case "text":
                {
                    var options = ParseOptions(args, positionalCount: 1, requireOutput: true);
                    if (options.RotateRows != 0)
                    {
                        throw new ArgumentException("--rotate applies only to music score commands.");
                    }
                    var rendered = JapaneseTextRenderer.Render(options.Positionals[0], options.Width);
                    WriteFile(options.Output!, rendered.Png);
                    Console.WriteLine($"Rendered Unicode text {rendered.Width}×{rendered.Height} PNG to {options.Output}");
                    return 0;
                }

                case "harmonica":
                {
                    var options = ParseOptions(args, positionalCount: 1, requireOutput: true);
                    if (options.RotateRows != 0)
                    {
                        throw new ArgumentException("Harmonica tabs do not use --rotate.");
                    }
                    var rendered = HarmonicaTabRenderer.Render(options.Positionals[0], options.Width);
                    WriteFile(options.Output!, rendered.Png);
                    Console.WriteLine($"Rendered harmonica tab {rendered.Width}×{rendered.Height} PNG to {options.Output}");
                    return 0;
                }

                case "raster-harmonica":
                {
                    var options = ParseOptions(args, positionalCount: 1, requireOutput: true);
                    if (options.RotateRows != 0)
                    {
                        throw new ArgumentException("Harmonica tabs do not use --rotate.");
                    }
                    var rendered = HarmonicaTabRenderer.Render(options.Positionals[0], options.Width);
                    var raster = MonochromeRasterEncoder.Encode(rendered);
                    WriteFile(options.Output!, raster);
                    Console.WriteLine($"Created harmonica-tab raster {rendered.Width}×{rendered.Height} at {options.Output}");
                    return 0;
                }

                case "raster":
                {
                    var options = ParseOptions(args, positionalCount: 1, requireOutput: true);
                    var rendered = MusicScoreRenderer.Render(options.Positionals[0], options.Width, options.RotateRows);
                    var raster = MonochromeRasterEncoder.Encode(rendered);
                    WriteFile(options.Output!, raster);
                    Console.WriteLine($"Created {raster.Length} monochrome raster bytes ({rendered.Width}×{rendered.Height}) at {options.Output}");
                    return 0;
                }

                case "print":
                {
                    var options = ParseOptions(args, positionalCount: 2, requireOutput: false);
                    if (options.Width != 384)
                    {
                        throw new ArgumentException("MXW01 printing requires --width 384.");
                    }
                    var rendered = MusicScoreRenderer.Render(options.Positionals[1], options.Width, options.RotateRows);
                    if (rendered.Width != 384 || rendered.Height > ushort.MaxValue)
                    {
                        throw new InvalidDataException("The rendered score must be 384 pixels wide and no taller than 65535 rows.");
                    }
                    var raster = MonochromeRasterEncoder.Encode(rendered);
                    return await SendRasterAsync(options.Positionals[0], raster);
                }

                case "print-text":
                {
                    var options = ParseOptions(args, positionalCount: 2, requireOutput: false);
                    if (options.Width != 384 || options.RotateRows != 0)
                    {
                        throw new ArgumentException("MXW01 text printing requires --width 384 and does not use --rotate.");
                    }
                    var rendered = JapaneseTextRenderer.Render(options.Positionals[1], options.Width);
                    if (rendered.Height > ushort.MaxValue)
                    {
                        throw new InvalidDataException("Text image exceeds the MXW01 65535-row limit.");
                    }
                    var raster = MonochromeRasterEncoder.Encode(rendered);
                    return await SendRasterAsync(options.Positionals[0], raster);
                }


                case "print-harmonica":
                {
                    var options = ParseOptions(args, positionalCount: 2, requireOutput: false);
                    if (options.Width != 384 || options.RotateRows != 0)
                    {
                        throw new ArgumentException("MXW01 harmonica printing requires --width 384 and does not use --rotate.");
                    }
                    var rendered = HarmonicaTabRenderer.Render(options.Positionals[1], options.Width);
                    if (rendered.Height > ushort.MaxValue)
                    {
                        throw new InvalidDataException("Harmonica tab image exceeds the MXW01 65535-row limit.");
                    }
                    var raster = MonochromeRasterEncoder.Encode(rendered);
                    return await SendRasterAsync(options.Positionals[0], raster);
                }

                default:
                    throw new ArgumentException($"Unknown command '{args[0]}'.");
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Error: {error.Message}");
            return 1;
        }
    }

    private static CliOptions ParseOptions(string[] args, int positionalCount, bool requireOutput)
    {
        var positionals = new List<string>();
        string? output = null;
        var width = DefaultWidth;
        var rotateRows = 0;

        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--width":
                    if (++index >= args.Length || !int.TryParse(args[index], out width))
                    {
                        throw new ArgumentException("--width requires a pixel width, such as 384 or 576.");
                    }
                    break;
                case "--rotate":
                    if (++index >= args.Length || !int.TryParse(args[index], out rotateRows) || rotateRows is not (1 or 2))
                    {
                        throw new ArgumentException("--rotate accepts 1 (standard notation) or 2 (standard notation plus guitar tablature).");
                    }
                    break;

                case "--output":
                case "-o":
                    if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                    {
                        throw new ArgumentException("--output requires a file path.");
                    }
                    output = args[index];
                    break;

                default:
                    if (args[index].StartsWith("-", StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"Unknown option '{args[index]}'.");
                    }
                    positionals.Add(args[index]);
                    break;
            }
        }

        if (positionals.Count != positionalCount)
        {
            throw new ArgumentException("The command has missing or extra positional arguments.");
        }
        if (requireOutput && output is null)
        {
            throw new ArgumentException("This command requires --output <file>.");
        }
        if (width is < MinimumWidth or > MaximumWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"Width must be between {MinimumWidth} and {MaximumWidth} pixels.");
        }

        if (rotateRows > 0 && width != 384)
        {
            throw new ArgumentException("Rotated MXW01 output requires --width 384.");
        }
        return new CliOptions(positionals, output, width, rotateRows);
    }

    private static async Task<int> SendRasterAsync(string deviceName, byte[] raster)
    {
        var rasterPath = Path.Combine(Path.GetTempPath(), $"ThermalMusicPrinter-{Guid.NewGuid():N}.bin");
        try
        {
            await File.WriteAllBytesAsync(rasterPath, raster);
            return await BluetoothBridge.RunAsync(["send", deviceName, rasterPath]);
        }
        finally
        {
            File.Delete(rasterPath);
        }
    }
    private static void WriteFile(string path, byte[] bytes)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("ThermalMusicPrinter — render scores and Unicode text for an MXW01 BLE printer");
        Console.WriteLine();
        Console.WriteLine("  ThermalMusicPrinter devices");
        Console.WriteLine("  ThermalMusicPrinter render <score-file> --output <preview.png> [--width 384] [--rotate 1|2]");
        Console.WriteLine("  ThermalMusicPrinter raster <score-file> --output <image.bin> [--width 384] [--rotate 1|2]");
        Console.WriteLine("  ThermalMusicPrinter print <BLE-device-name> <score-file> [--width 384] [--rotate 1|2]");
        Console.WriteLine("  ThermalMusicPrinter text <utf8-file> --output <preview.png> [--width 384]");
        Console.WriteLine("  ThermalMusicPrinter print-text <BLE-device-name> <utf8-file>");
        Console.WriteLine();
        Console.WriteLine("Rotate prints notes along the paper feed direction: 1=standard staff, 2=standard staff plus guitar tablature.");
        Console.WriteLine("Music inputs: alphaTex, Guitar Pro, MusicXML, and Capella. Text input is UTF-8.");
        Console.WriteLine("The MXW01 requires a 384-pixel-wide, one-bit raster.");
        Console.WriteLine("  ThermalMusicPrinter harmonica <tab-file> --output <preview.png> [--width 384]");
        Console.WriteLine("  ThermalMusicPrinter raster-harmonica <tab-file> --output <image.bin> [--width 384]");
        Console.WriteLine("  ThermalMusicPrinter print-harmonica <BLE-device-name> <tab-file>");
        Console.WriteLine();
        Console.WriteLine("Harmonica tabs: N=blow, -N=draw, apostrophes=half-step bends; use spaces between notes and | for bars.");
    }

    private sealed record CliOptions(List<string> Positionals, string? Output, int Width, int RotateRows);
}
