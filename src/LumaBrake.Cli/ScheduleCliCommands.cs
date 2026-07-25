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
            Console.Error.WriteLine(ScheduleUsage);
            return 2;
        }

        switch (args[1].ToLowerInvariant())
        {
            case "list":
                Console.WriteLine($"Schedules: {(settings.Schedules.Enabled ? "enabled" : "disabled")}");
                foreach (var entry in settings.Schedules.Clamp().Entries)
                {
                    Console.WriteLine(
                        $"{ScheduleCliFormatting.KeyValue("id", entry.Id)} " +
                        $"{ScheduleCliFormatting.KeyValue("name", entry.Name)} " +
                        $"enabled=\"{(entry.Enabled ? "yes" : "no")}\" " +
                        $"days=\"{entry.DayMask.ToString(CultureInfo.InvariantCulture)}\" " +
                        $"{ScheduleCliFormatting.KeyValue("dayNames", ScheduleCliFormatting.FormatDayMask(entry.DayMask))} " +
                        $"start=\"{ScheduleCliFormatting.FormatClock(entry.StartMinuteOfDay)}\" " +
                        $"end=\"{ScheduleCliFormatting.FormatClock(entry.EndMinuteOfDay)}\" " +
                        $"manualDimming=\"{Math.Round(entry.ManualDimmingOpacity * 100).ToString("0", CultureInfo.InvariantCulture)}\" " +
                        $"{ScheduleCliFormatting.KeyValue("hardwareBrightness", ScheduleCliFormatting.FormatHardwareBrightness(entry.HardwareBrightness))} " +
                        $"{ScheduleCliFormatting.KeyValue("tint", ScheduleCliFormatting.FormatTint(entry.Tint))}");
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

            case "set":
                return SetSchedule(args, store, settings);

            case "remove":
                return RemoveSchedule(args, store, settings);

            case "hardware":
                return SetScheduleHardwareBrightness(args, store, settings);

            case "enable-entry":
                return SetScheduleEntryEnabled(args, store, settings, enabled: true);

            case "disable-entry":
                return SetScheduleEntryEnabled(args, store, settings, enabled: false);

            default:
                Console.Error.WriteLine(ScheduleUsage);
                return 2;
        }
    }

    public static int RunStartupRestore(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 2)
        {
            Console.Error.WriteLine("Usage: lumabrake startup-restore <status|enable|disable|hardware>");
            return 2;
        }

        switch (args[1].ToLowerInvariant())
        {
            case "status":
                Console.WriteLine($"Startup restoration: {(settings.StartupRestoration.Enabled ? "enabled" : "disabled")}");
                Console.WriteLine($"Apply schedules at startup: {(settings.StartupRestoration.ApplySchedules ? "yes" : "no")}");
                Console.WriteLine($"Hardware brightness target: {ScheduleCliFormatting.FormatHardwareBrightness(settings.StartupRestoration.HardwareBrightness)}");
                return 0;

            case "enable":
                store.Save(settings with { StartupRestoration = settings.StartupRestoration with { Enabled = true } });
                Console.WriteLine("Startup restoration enabled.");
                return 0;

            case "disable":
                store.Save(settings with { StartupRestoration = settings.StartupRestoration with { Enabled = false } });
                Console.WriteLine("Startup restoration disabled.");
                return 0;

            case "hardware":
                return SetStartupRestoreHardwareBrightness(args, store, settings);

            default:
                Console.Error.WriteLine("Usage: lumabrake startup-restore <status|enable|disable|hardware>");
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
                ? ScheduleCliParsing.ParseTintOptions(args.Count >= 7 ? ["tint", args[5], args[6]] : ["tint", args[5]])
                : new TintOptions { Enabled = false };
            var nightSchedule = new ScheduledEffect
            {
                Id = "night",
                Name = "Night",
                Enabled = true,
                DayMask = ScheduleDayMask.EveryDay,
                StartMinuteOfDay = ScheduleCliParsing.ParseClockMinute(args[2]),
                EndMinuteOfDay = ScheduleCliParsing.ParseClockMinute(args[3]),
                ManualDimmingOpacity = ScheduleCliParsing.ParseManualOpacity(args[4]),
                Tint = tint
            }.Clamp();

            if (nightSchedule.StartMinuteOfDay == nightSchedule.EndMinuteOfDay)
            {
                throw new ArgumentException("Schedule start and end times must be different.");
            }

            var otherEntries = settings.Schedules.Clamp().Entries
                .Where(entry => !string.Equals(entry.Id, "night", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var entries = new ScheduledEffect[otherEntries.Length + 1];
            entries[0] = nightSchedule;
            Array.Copy(otherEntries, 0, entries, 1, otherEntries.Length);

            store.Save(settings with { Schedules = settings.Schedules with { Enabled = true, Entries = entries } });
            Console.WriteLine($"Night schedule saved: {ScheduleCliFormatting.FormatClock(nightSchedule.StartMinuteOfDay)}-{ScheduleCliFormatting.FormatClock(nightSchedule.EndMinuteOfDay)}, {Math.Round(nightSchedule.ManualDimmingOpacity * 100).ToString("0", CultureInfo.InvariantCulture)}%.");
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Usage: lumabrake schedule night <start HH:mm> <end HH:mm> <0-90|off> [off|warm|neutral] [0-60]");
            return 2;
        }
    }

    private static int SetSchedule(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 7 || string.IsNullOrWhiteSpace(args[2]))
        {
            Console.Error.WriteLine("Usage: lumabrake schedule set <id> <days> <start HH:mm> <end HH:mm> <0-90|off> [off|warm|neutral] [0-60] [name]");
            return 2;
        }

        try
        {
            var tintAndName = ScheduleCliParsing.ParseTintAndName(args, 7);
            var schedule = new ScheduledEffect
            {
                Id = args[2],
                Name = string.IsNullOrWhiteSpace(tintAndName.Name) ? args[2] : tintAndName.Name,
                Enabled = true,
                DayMask = ScheduleCliParsing.ParseDayMask(args[3]),
                StartMinuteOfDay = ScheduleCliParsing.ParseClockMinute(args[4]),
                EndMinuteOfDay = ScheduleCliParsing.ParseClockMinute(args[5]),
                ManualDimmingOpacity = ScheduleCliParsing.ParseManualOpacity(args[6]),
                Tint = tintAndName.Tint
            }.Clamp();

            if (schedule.StartMinuteOfDay == schedule.EndMinuteOfDay)
            {
                throw new ArgumentException("Schedule start and end times must be different.");
            }

            store.Save(settings with { Schedules = settings.Schedules.Upsert(schedule) with { Enabled = true } });
            Console.WriteLine($"Schedule saved: {schedule.Id}");
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Usage: lumabrake schedule set <id> <days> <start HH:mm> <end HH:mm> <0-90|off> [off|warm|neutral] [0-60] [name]");
            return 2;
        }
    }

    private static int RemoveSchedule(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 3 || string.IsNullOrWhiteSpace(args[2]))
        {
            Console.Error.WriteLine("Usage: lumabrake schedule remove <id>");
            return 2;
        }

        var id = args[2].Trim();
        if (settings.Schedules.Find(id) is null)
        {
            Console.Error.WriteLine($"Schedule not found: {id}");
            return 1;
        }

        store.Save(settings with { Schedules = settings.Schedules.Remove(id) });
        Console.WriteLine($"Schedule removed: {id}");
        return 0;
    }

    private static int SetScheduleHardwareBrightness(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 4 || string.IsNullOrWhiteSpace(args[2]))
        {
            Console.Error.WriteLine($"Usage: lumabrake schedule hardware <id> <off|display-device physical-index 0-100 [{ScheduleCliParsing.SoftwareFallbackFlag}]>");
            return 2;
        }

        var id = args[2].Trim();
        var schedule = settings.Schedules.Find(id);
        if (schedule is null)
        {
            Console.Error.WriteLine($"Schedule not found: {id}");
            return 1;
        }

        try
        {
            var target = ScheduleCliParsing.ParseHardwareBrightnessTarget(args, 3);
            store.Save(settings with
            {
                Schedules = settings.Schedules.Upsert(schedule with { HardwareBrightness = target })
            });
            Console.WriteLine($"Schedule hardware brightness saved: {schedule.Id}");
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine($"Usage: lumabrake schedule hardware <id> <off|display-device physical-index 0-100 [{ScheduleCliParsing.SoftwareFallbackFlag}]>");
            return 2;
        }
    }

    private static int SetStartupRestoreHardwareBrightness(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 3)
        {
            Console.Error.WriteLine($"Usage: lumabrake startup-restore hardware <off|display-device physical-index 0-100 [{ScheduleCliParsing.SoftwareFallbackFlag}]>");
            return 2;
        }

        try
        {
            var target = ScheduleCliParsing.ParseHardwareBrightnessTarget(args, 2);
            store.Save(settings with
            {
                StartupRestoration = settings.StartupRestoration with { HardwareBrightness = target }
            });
            Console.WriteLine($"Startup restoration hardware brightness: {ScheduleCliFormatting.FormatHardwareBrightness(target)}");
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine($"Usage: lumabrake startup-restore hardware <off|display-device physical-index 0-100 [{ScheduleCliParsing.SoftwareFallbackFlag}]>");
            return 2;
        }
    }

    private static int SetScheduleEntryEnabled(
        IReadOnlyList<string> args,
        SettingsStore store,
        LumaBrakeSettings settings,
        bool enabled)
    {
        if (args.Count < 3 || string.IsNullOrWhiteSpace(args[2]))
        {
            Console.Error.WriteLine(enabled
                ? "Usage: lumabrake schedule enable-entry <id>"
                : "Usage: lumabrake schedule disable-entry <id>");
            return 2;
        }

        var id = args[2].Trim();
        var schedule = settings.Schedules.Find(id);
        if (schedule is null)
        {
            Console.Error.WriteLine($"Schedule not found: {id}");
            return 1;
        }

        store.Save(settings with { Schedules = settings.Schedules.SetEntryEnabled(id, enabled) });
        Console.WriteLine($"Schedule {(enabled ? "enabled" : "disabled")}: {schedule.Id}");
        return 0;
    }

    private const string ScheduleUsage = "Usage: lumabrake schedule <list|enable|disable|clear|night|set|remove|hardware|enable-entry|disable-entry>";
}
