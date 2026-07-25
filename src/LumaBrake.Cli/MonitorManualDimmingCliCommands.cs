using System.Globalization;
using LumaBrake.Domain.Effects;
using LumaBrake.Persistence;

namespace LumaBrake.Cli;

internal static class MonitorManualDimmingCliCommands
{
    public static int Run(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 3)
        {
            Console.Error.WriteLine("Usage: lumabrake dim monitor <list|set|remove>");
            return 2;
        }

        return args[2].ToLowerInvariant() switch
        {
            "list" => ListMonitorManualDimming(settings),
            "set" => SetMonitorManualDimming(args, store, settings),
            "remove" => RemoveMonitorManualDimming(args, store, settings),
            _ => PrintUsage()
        };
    }

    private static int ListMonitorManualDimming(LumaBrakeSettings settings)
    {
        foreach (var entry in settings.MonitorManualDimming.Clamp().Entries)
        {
            Console.WriteLine(
                $"{ScheduleCliFormatting.KeyValue("monitor", entry.MonitorId)} " +
                $"{ScheduleCliFormatting.KeyValue("manualDimming", FormatPercent(entry.Opacity))}");
        }

        return 0;
    }

    private static int SetMonitorManualDimming(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 5 || string.IsNullOrWhiteSpace(args[3]))
        {
            Console.Error.WriteLine("Usage: lumabrake dim monitor set <monitor-id|device-name> <0-90|off>");
            return 2;
        }

        try
        {
            var opacity = ScheduleCliParsing.ParseManualOpacity(args[4]);
            var entry = new MonitorManualDimmingOverride { MonitorId = args[3], Opacity = opacity }.Clamp();
            store.Save(settings with { MonitorManualDimming = settings.MonitorManualDimming.Upsert(entry) });
            Console.WriteLine($"Monitor manual dimming saved: {entry.MonitorId} {FormatPercent(entry.Opacity)}%");
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Usage: lumabrake dim monitor set <monitor-id|device-name> <0-90|off>");
            return 2;
        }
    }

    private static int RemoveMonitorManualDimming(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 4 || string.IsNullOrWhiteSpace(args[3]))
        {
            Console.Error.WriteLine("Usage: lumabrake dim monitor remove <monitor-id|device-name>");
            return 2;
        }

        var monitorId = args[3].Trim();
        if (settings.MonitorManualDimming.Find(monitorId) is null)
        {
            Console.Error.WriteLine($"Monitor manual dimming not found: {monitorId}");
            return 1;
        }

        store.Save(settings with { MonitorManualDimming = settings.MonitorManualDimming.Remove(monitorId) });
        Console.WriteLine($"Monitor manual dimming removed: {monitorId}");
        return 0;
    }

    private static int PrintUsage()
    {
        Console.Error.WriteLine("Usage: lumabrake dim monitor <list|set|remove>");
        return 2;
    }

    private static string FormatPercent(double opacity) =>
        Math.Round(opacity * 100).ToString("0", CultureInfo.InvariantCulture);
}
