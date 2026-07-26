using System.Globalization;
using LumaBrake.Analysis.Frames;
using LumaBrake.Capture;
using LumaBrake.Cli;
using LumaBrake.Diagnostics;
using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Monitors;
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
        Console.WriteLine($"Manual software dimming: {Math.Round(settings.ManualDimmingOpacity * 100).ToString("0", CultureInfo.InvariantCulture)}%");
        Console.WriteLine(settings.Tint.Enabled
            ? $"Tint: #{settings.Tint.Red:X2}{settings.Tint.Green:X2}{settings.Tint.Blue:X2} {Math.Round(settings.Tint.Opacity * 100).ToString("0", CultureInfo.InvariantCulture)}%"
            : "Tint: off");
        Console.WriteLine($"Schedules: {(settings.Schedules.Enabled ? "enabled" : "disabled")}, {settings.Schedules.Entries.Length.ToString(CultureInfo.InvariantCulture)} configured");
        Console.WriteLine($"Profiles: {settings.Profiles.Clamp().Entries.Length.ToString(CultureInfo.InvariantCulture)} saved");
        Console.WriteLine($"Startup restoration: {(settings.StartupRestoration.Enabled ? "enabled" : "disabled")}");
        var focusDimming = settings.FocusDimming.Clamp();
        Console.WriteLine($"Focus dimming: {(focusDimming.Enabled ? "enabled" : "disabled")} {Math.Round(focusDimming.Opacity * 100).ToString("0", CultureInfo.InvariantCulture)}%");
        var activeSchedule = settings.Schedules.ActiveAt(DateTimeOffset.Now);
        Console.WriteLine(activeSchedule is null ? "Active schedule: none" : $"Active schedule: {activeSchedule.Name}");
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
        store.Save(SafeModeSettings.Apply(settings));
        Console.WriteLine("Safe mode settings saved.");
        return 0;

    case "dim":
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: lumabrake dim <0-90|off>");
            return 2;
        }

        double manualOpacity;
        try
        {
            manualOpacity = ParseManualOpacity(args[1]);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Usage: lumabrake dim <0-90|off>");
            return 2;
        }

        store.Save(settings with { ManualDimmingOpacity = manualOpacity });
        Console.WriteLine($"Manual software dimming: {Math.Round(manualOpacity * 100).ToString("0", CultureInfo.InvariantCulture)}%");
        return 0;

    case "tint":
        TintOptions tintOptions;
        try
        {
            tintOptions = ParseTintOptions(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Usage: lumabrake tint <off|warm|neutral> [0-60]");
            return 2;
        }

        store.Save(settings with { Tint = tintOptions });
        Console.WriteLine(tintOptions.Enabled
            ? $"Tint: #{tintOptions.Red:X2}{tintOptions.Green:X2}{tintOptions.Blue:X2} {Math.Round(tintOptions.Opacity * 100).ToString("0", CultureInfo.InvariantCulture)}%"
            : "Tint: off");
        return 0;

    case "focus":
        return RunFocusCommand(args, store, settings);

    case "profile":
        return ProfileCliCommands.Run(args, store, settings);

    case "capture-test":
        var primaryMonitor = PrimaryMonitor.Get();
        using (var capture = new GdiMonitorCapture())
        {
            var frame = capture.Capture(primaryMonitor);
            var result = new Bgra32FrameAnalyzer().Analyze(frame.BgraPixels, frame.Metadata);
            Console.WriteLine($"Source monitor: {frame.Metadata.Source}");
            Console.WriteLine($"Source bounds: {primaryMonitor.Bounds.Left},{primaryMonitor.Bounds.Top} {primaryMonitor.Bounds.Width}x{primaryMonitor.Bounds.Height}");
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
                    $"{KeyValue("device", monitor.DeviceName)} " +
                    $"{KeyValue("id", monitor.StableId)} " +
                    $"role=\"{(monitor.IsPrimary ? "primary" : "secondary")}\" " +
                    $"{KeyValue("bounds", $"{monitor.Bounds.Left},{monitor.Bounds.Top} {monitor.Bounds.Width}x{monitor.Bounds.Height}")} " +
                    $"{KeyValue("name", monitor.DisplayName)}");
            }

            return 0;
        }

        Console.Error.WriteLine("Usage: lumabrake monitor list");
        return 2;

    case "hardware":
        return HardwareCliCommands.Run(args, store, settings);

    case "schedule":
        return ScheduleCliCommands.RunSchedule(args, store, settings);

    case "startup-restore":
        return ScheduleCliCommands.RunStartupRestore(args, store, settings);

    case "diagnostics":
        if (args.Length >= 2 && string.Equals(args[1], "export", StringComparison.OrdinalIgnoreCase))
        {
            var outputPath = args.Length >= 3
                ? args[2]
                : Path.Combine(Environment.CurrentDirectory, $"lumabrake-diagnostics-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.zip");
            string? monitorError = null;
            IReadOnlyList<MonitorDescriptor> monitors;

            try
            {
                monitors = new WindowsMonitorEnumerator().GetMonitors();
            }
            catch (InvalidOperationException ex)
            {
                monitors = [];
                monitorError = ex.Message;
            }

            var exportedPath = new DiagnosticPackageExporter().Export(
                new DiagnosticPackageContent(settings, monitors, monitorError),
                outputPath);

            Console.WriteLine($"Diagnostics package written: {exportedPath}");
            return 0;
        }

        Console.Error.WriteLine("Usage: lumabrake diagnostics export [output.zip]");
        return 2;

    default:
        Console.Error.WriteLine("Usage: lumabrake status|enable|disable|safe-mode|dim <0-90|off>|tint <off|warm|neutral> [0-60]|focus <status|enable|disable|opacity>|profile <list|save|apply|remove>|capture-test|monitor list|hardware <list|brightness|writes|blacklist>|schedule <list|enable|disable|clear|night>|startup-restore <status|enable|disable>|diagnostics export [output.zip]");
        return 2;
}

static int RunFocusCommand(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
{
    if (args.Count < 2)
    {
        Console.Error.WriteLine("Usage: lumabrake focus <status|enable|disable|opacity> [0-60|off]");
        return 2;
    }

    var options = settings.FocusDimming.Clamp();
    switch (args[1].ToLowerInvariant())
    {
        case "status":
            Console.WriteLine($"Focus dimming: {(options.Enabled ? "enabled" : "disabled")}");
            Console.WriteLine($"Focus dimming opacity: {Math.Round(options.Opacity * 100).ToString("0", CultureInfo.InvariantCulture)}%");
            return 0;

        case "enable":
            store.Save(settings with { FocusDimming = options with { Enabled = true } });
            Console.WriteLine("Focus dimming enabled.");
            return 0;

        case "disable":
            store.Save(settings with { FocusDimming = options with { Enabled = false } });
            Console.WriteLine("Focus dimming disabled.");
            return 0;

        case "opacity":
            if (args.Count < 3)
            {
                Console.Error.WriteLine("Usage: lumabrake focus opacity <0-60|off>");
                return 2;
            }

            double opacity;
            try
            {
                opacity = ParseFocusOpacity(args[2]);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine("Usage: lumabrake focus opacity <0-60|off>");
                return 2;
            }

            store.Save(settings with { FocusDimming = options with { Enabled = options.Enabled && opacity > 0, Opacity = opacity } });
            Console.WriteLine(opacity > 0
                ? $"Focus dimming opacity: {Math.Round(opacity * 100).ToString("0", CultureInfo.InvariantCulture)}%"
                : "Focus dimming disabled.");
            return 0;

        default:
            Console.Error.WriteLine("Usage: lumabrake focus <status|enable|disable|opacity> [0-60|off]");
            return 2;
    }
}

static double ParseManualOpacity(string value)
{
    if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
    {
        return 0;
    }

    var normalized = value.Trim().TrimEnd('%');
    if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
    {
        throw new ArgumentException("Manual dimming must be a percentage from 0 to 90 or 'off'.", nameof(value));
    }

    var opacity = parsed > 1 ? parsed / 100.0 : parsed;
    return Math.Clamp(opacity, 0, 0.90);
}

static double ParseFocusOpacity(string value)
{
    if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
    {
        return 0;
    }

    var normalized = value.Trim().TrimEnd('%');
    if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
    {
        throw new ArgumentException("Focus dimming opacity must be a percentage from 0 to 60.", nameof(value));
    }

    var opacity = parsed > 1 ? parsed / 100.0 : parsed;
    return Math.Clamp(opacity, 0, 0.60);
}

static string KeyValue(string key, string value)
{
    var escaped = value
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);

    return $"{key}=\"{escaped}\"";
}

static TintOptions ParseTintOptions(IReadOnlyList<string> args)
{
    if (args.Count < 2)
    {
        throw new ArgumentException("Tint mode is required.", nameof(args));
    }

    if (string.Equals(args[1], "off", StringComparison.OrdinalIgnoreCase))
    {
        return new TintOptions { Enabled = false };
    }

    var opacity = args.Count >= 3 ? ParseTintOpacity(args[2]) : 0.12;
    return args[1].ToLowerInvariant() switch
    {
        "warm" => new TintOptions { Enabled = true, Red = 255, Green = 214, Blue = 170, Opacity = opacity },
        "neutral" => new TintOptions { Enabled = true, Red = 255, Green = 244, Blue = 230, Opacity = opacity },
        _ => throw new ArgumentException("Tint mode must be 'off', 'warm', or 'neutral'.", nameof(args))
    };
}

static double ParseTintOpacity(string value)
{
    var normalized = value.Trim().TrimEnd('%');
    if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
    {
        throw new ArgumentException("Tint opacity must be a percentage from 0 to 60.", nameof(value));
    }

    var opacity = parsed > 1 ? parsed / 100.0 : parsed;
    return Math.Clamp(opacity, 0, 0.60);
}
