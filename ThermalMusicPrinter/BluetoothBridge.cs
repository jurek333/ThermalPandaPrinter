using System.Diagnostics;

namespace ThermalMusicPrinter;

internal static class BluetoothBridge
{
    public static async Task<int> RunAsync(IReadOnlyList<string> arguments)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Bluetooth printing currently uses the macOS CoreBluetooth bridge.");
        }

        var packagePath = Path.Combine(AppContext.BaseDirectory, "BluetoothSender");
        if (!File.Exists(Path.Combine(packagePath, "Package.swift")))
        {
            throw new FileNotFoundException("The bundled Bluetooth sender sources are missing.", packagePath);
        }
        if (!File.Exists("/usr/bin/swift"))
        {
            throw new FileNotFoundException("Swift is required to build the macOS Bluetooth sender.", "/usr/bin/swift");
        }

        var startInfo = new ProcessStartInfo("/usr/bin/swift")
        {
            WorkingDirectory = packagePath,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--package-path");
        startInfo.ArgumentList.Add(packagePath);
        startInfo.ArgumentList.Add("ThermalMusicBluetoothSender");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the macOS Bluetooth sender.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(16));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The Bluetooth sender did not finish within sixteen minutes.");
        }


        return process.ExitCode;
    }
}
