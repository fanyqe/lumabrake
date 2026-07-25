using System.Globalization;
using LumaBrake.Analysis.Frames;
using LumaBrake.Capture;
using LumaBrake.Persistence;
using LumaBrake.Platform.Windows;

var store = new SettingsStore();
var settings = store.Load();
var command = args.Length == 0 ? "status" : args[0].ToLowerInvariant();

switch (command)
{
    case "status":
        Console.WriteLine(settings.Enabled ? "LumaBrake settings: enabled" : "LumaBrake settings: disabled");
        Console.WriteLine($"Maximum software dimming: {Math.Round(settings.UserMaximumOpacity * 100).ToString("0", CultureInfo.InvariantCulture)}%");
        return 0;

    case "enable":
        store.Save(settings with { Enabled = true });
        Console.WriteLine("LumaBrake will start enabled.");
        return 0;

    case "disable":
        store.Save(settings with { Enabled = false });
        Console.WriteLine("LumaBrake will start disabled.");
        return 0;

    case "safe-mode":
        store.Save(settings with { Enabled = false, UserMaximumOpacity = 0.25 });
        Console.WriteLine("Safe mode settings saved.");
        return 0;

    case "capture-test":
        using (var capture = new GdiPrimaryMonitorCapture())
        {
            var frame = capture.Capture();
            var result = new Bgra32FrameAnalyzer().Analyze(frame.BgraPixels, frame.Metadata);
            var monitor = PrimaryMonitor.Get();
            Console.WriteLine($"Source monitor: {frame.Metadata.Source}");
            Console.WriteLine($"Source bounds: {monitor.Bounds.Left},{monitor.Bounds.Top} {monitor.Bounds.Width}x{monitor.Bounds.Height}");
            Console.WriteLine($"Captured frame: {frame.Metadata.Width}x{frame.Metadata.Height}");
            Console.WriteLine($"Average luminance: {result.AverageLuminance.ToString("0.000", CultureInfo.InvariantCulture)}");
            Console.WriteLine($"95th percentile luminance: {result.Percentile95Luminance.ToString("0.000", CultureInfo.InvariantCulture)}");
        }

        return 0;

    case "monitor":
        if (args.Length >= 2 && string.Equals(args[1], "list", StringComparison.OrdinalIgnoreCase))
        {
            var monitors = new WindowsMonitorEnumerator().GetMonitors();
            foreach (var monitor in monitors)
            {
                Console.WriteLine(
                    $"device=\"{monitor.DeviceName}\" " +
                    $"id=\"{monitor.StableId}\" " +
                    $"role=\"{(monitor.IsPrimary ? "primary" : "secondary")}\" " +
                    $"bounds=\"{monitor.Bounds.Left},{monitor.Bounds.Top} {monitor.Bounds.Width}x{monitor.Bounds.Height}\" " +
                    $"name=\"{monitor.DisplayName}\"");
            }

            return 0;
        }

        Console.Error.WriteLine("Usage: lumabrake monitor list");
        return 2;

    default:
        Console.Error.WriteLine("Usage: lumabrake status|enable|disable|safe-mode|capture-test|monitor list");
        return 2;
}
