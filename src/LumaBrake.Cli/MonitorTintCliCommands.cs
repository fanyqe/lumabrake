using System.Globalization;
using LumaBrake.Domain.Effects;
using LumaBrake.Persistence;

namespace LumaBrake.Cli;

internal static class MonitorTintCliCommands
{
    public static int Run(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 3)
        {
            Console.Error.WriteLine("Usage: lumabrake tint monitor <list|set|remove>");
            return 2;
        }

        return args[2].ToLowerInvariant() switch
        {
            "list" => ListMonitorTints(settings),
            "set" => SetMonitorTint(args, store, settings),
            "remove" => RemoveMonitorTint(args, store, settings),
            _ => PrintUsage()
        };
    }

    private static int ListMonitorTints(LumaBrakeSettings settings)
    {
        foreach (var entry in settings.MonitorTints.Clamp().Entries)
        {
            Console.WriteLine(
                $"{ScheduleCliFormatting.KeyValue("monitor", entry.MonitorId)} " +
                $"{ScheduleCliFormatting.KeyValue("tint", ScheduleCliFormatting.FormatTint(entry.Tint))}");
        }

        return 0;
    }

    private static int SetMonitorTint(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 5 || string.IsNullOrWhiteSpace(args[3]))
        {
            Console.Error.WriteLine("Usage: lumabrake tint monitor set <monitor-id|device-name> <off|warm|neutral> [0-60]");
            return 2;
        }

        try
        {
            var tint = ParseTintOptions(args);
            var entry = new MonitorTintOverride { MonitorId = args[3], Tint = tint }.Clamp();
            store.Save(settings with { MonitorTints = settings.MonitorTints.Upsert(entry) });
            Console.WriteLine($"Monitor tint saved: {entry.MonitorId}");
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Usage: lumabrake tint monitor set <monitor-id|device-name> <off|warm|neutral> [0-60]");
            return 2;
        }
    }

    private static int RemoveMonitorTint(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 4 || string.IsNullOrWhiteSpace(args[3]))
        {
            Console.Error.WriteLine("Usage: lumabrake tint monitor remove <monitor-id|device-name>");
            return 2;
        }

        var monitorId = args[3].Trim();
        if (settings.MonitorTints.Find(monitorId) is null)
        {
            Console.Error.WriteLine($"Monitor tint not found: {monitorId}");
            return 1;
        }

        store.Save(settings with { MonitorTints = settings.MonitorTints.Remove(monitorId) });
        Console.WriteLine($"Monitor tint removed: {monitorId}");
        return 0;
    }

    private static int PrintUsage()
    {
        Console.Error.WriteLine("Usage: lumabrake tint monitor <list|set|remove>");
        return 2;
    }

    private static TintOptions ParseTintOptions(IReadOnlyList<string> args)
    {
        if (string.Equals(args[4], "off", StringComparison.OrdinalIgnoreCase))
        {
            return new TintOptions { Enabled = false };
        }

        var opacity = args.Count >= 6 ? ParseTintOpacity(args[5]) : 0.12;
        return args[4].ToLowerInvariant() switch
        {
            "warm" => new TintOptions { Enabled = true, Red = 255, Green = 214, Blue = 170, Opacity = opacity },
            "neutral" => new TintOptions { Enabled = true, Red = 255, Green = 244, Blue = 230, Opacity = opacity },
            _ => throw new ArgumentException("Tint mode must be 'off', 'warm', or 'neutral'.", nameof(args))
        };
    }

    private static double ParseTintOpacity(string value)
    {
        var normalized = value.Trim().TrimEnd('%');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException("Tint opacity must be a percentage from 0 to 60.", nameof(value));
        }

        var opacity = parsed > 1 ? parsed / 100.0 : parsed;
        return Math.Clamp(opacity, 0, 0.60);
    }

}
