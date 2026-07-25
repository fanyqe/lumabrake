using System.Globalization;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Schedules;
using LumaBrake.Persistence;

namespace LumaBrake.Cli;

internal static class ScheduleCliCommands
{
    public static int RunSchedule(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 2)
        {
            Console.Error.WriteLine("Usage: lumabrake schedule <list|enable|disable|clear|night>");
            return 2;
        }

        switch (args[1].ToLowerInvariant())
        {
            case "list":
                Console.WriteLine($"Schedules: {(settings.Schedules.Enabled ? "enabled" : "disabled")}");
                foreach (var entry in settings.Schedules.Entries.Select(entry => entry.Clamp()))
                {
                    Console.WriteLine(
                        $"{KeyValue("id", entry.Id)} " +
                        $"{KeyValue("name", entry.Name)} " +
                        $"enabled=\"{(entry.Enabled ? "yes" : "no")}\" " +
                        $"days=\"{entry.DayMask.ToString(CultureInfo.InvariantCulture)}\" " +
                        $"start=\"{FormatClock(entry.StartMinuteOfDay)}\" " +
                        $"end=\"{FormatClock(entry.EndMinuteOfDay)}\" " +
                        $"manualDimming=\"{Math.Round(entry.ManualDimmingOpacity * 100).ToString("0", CultureInfo.InvariantCulture)}\" " +
                        $"{KeyValue("tint", FormatTint(entry.Tint))}");
                }

                return 0;

            case "enable":
                store.Save(settings with { Schedules = settings.Schedules with { Enabled = true } });
                Console.WriteLine("Schedules enabled.");
                return 0;

            case "disable":
                store.Save(settings with { Schedules = settings.Schedules with { Enabled = false } });
                Console.WriteLine("Schedules disabled.");
                return 0;

            case "clear":
                store.Save(settings with { Schedules = settings.Schedules with { Enabled = false, Entries = [] } });
                Console.WriteLine("Schedules cleared.");
                return 0;

            case "night":
                return SaveNightSchedule(args, store, settings);

            default:
                Console.Error.WriteLine("Usage: lumabrake schedule <list|enable|disable|clear|night>");
                return 2;
        }
    }

    public static int RunStartupRestore(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 2)
        {
            Console.Error.WriteLine("Usage: lumabrake startup-restore <status|enable|disable>");
            return 2;
        }

        switch (args[1].ToLowerInvariant())
        {
            case "status":
                Console.WriteLine($"Startup restoration: {(settings.StartupRestoration.Enabled ? "enabled" : "disabled")}");
                Console.WriteLine($"Apply schedules at startup: {(settings.StartupRestoration.ApplySchedules ? "yes" : "no")}");
                return 0;

            case "enable":
                store.Save(settings with { StartupRestoration = settings.StartupRestoration with { Enabled = true } });
                Console.WriteLine("Startup restoration enabled.");
                return 0;

            case "disable":
                store.Save(settings with { StartupRestoration = settings.StartupRestoration with { Enabled = false } });
                Console.WriteLine("Startup restoration disabled.");
                return 0;

            default:
                Console.Error.WriteLine("Usage: lumabrake startup-restore <status|enable|disable>");
                return 2;
        }
    }

    private static int SaveNightSchedule(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 5)
        {
            Console.Error.WriteLine("Usage: lumabrake schedule night <start HH:mm> <end HH:mm> <0-90|off> [off|warm|neutral] [0-60]");
            return 2;
        }

        try
        {
            var tint = args.Count >= 6
                ? ParseTintOptions(args.Count >= 7 ? ["tint", args[5], args[6]] : ["tint", args[5]])
                : new TintOptions { Enabled = false };
            var nightSchedule = new ScheduledEffect
            {
                Id = "night",
                Name = "Night",
                Enabled = true,
                DayMask = ScheduleDayMask.EveryDay,
                StartMinuteOfDay = ParseClockMinute(args[2]),
                EndMinuteOfDay = ParseClockMinute(args[3]),
                ManualDimmingOpacity = ParseManualOpacity(args[4]),
                Tint = tint
            }.Clamp();

            if (nightSchedule.StartMinuteOfDay == nightSchedule.EndMinuteOfDay)
            {
                throw new ArgumentException("Schedule start and end times must be different.");
            }

            var otherEntries = settings.Schedules.Entries
                .Where(entry => !string.Equals(entry.Id, "night", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var entries = new ScheduledEffect[otherEntries.Length + 1];
            entries[0] = nightSchedule;
            Array.Copy(otherEntries, 0, entries, 1, otherEntries.Length);

            store.Save(settings with { Schedules = settings.Schedules with { Enabled = true, Entries = entries } });
            Console.WriteLine($"Night schedule saved: {FormatClock(nightSchedule.StartMinuteOfDay)}-{FormatClock(nightSchedule.EndMinuteOfDay)}, {Math.Round(nightSchedule.ManualDimmingOpacity * 100).ToString("0", CultureInfo.InvariantCulture)}%.");
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Usage: lumabrake schedule night <start HH:mm> <end HH:mm> <0-90|off> [off|warm|neutral] [0-60]");
            return 2;
        }
    }

    private static double ParseManualOpacity(string value)
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

    private static TintOptions ParseTintOptions(IReadOnlyList<string> args)
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

    private static int ParseClockMinute(string value)
    {
        if (!TimeOnly.TryParseExact(value, "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) &&
            !TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
        {
            throw new ArgumentException("Schedule time must use HH:mm format.", nameof(value));
        }

        return (time.Hour * 60) + time.Minute;
    }

    private static string FormatClock(int minuteOfDay)
    {
        var minute = Math.Clamp(minuteOfDay, 0, 1439);
        return $"{minute / 60:00}:{minute % 60:00}";
    }

    private static string FormatTint(TintOptions tint)
    {
        var effect = tint.ToEffect();
        return effect.Enabled
            ? $"#{effect.Red:X2}{effect.Green:X2}{effect.Blue:X2} {Math.Round(effect.Opacity * 100).ToString("0", CultureInfo.InvariantCulture)}%"
            : "off";
    }

    private static string KeyValue(string key, string value)
    {
        var escaped = value
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        return $"{key}=\"{escaped}\"";
    }
}
