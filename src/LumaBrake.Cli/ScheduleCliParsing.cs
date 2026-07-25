using System.Globalization;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Hardware;
using LumaBrake.Domain.Schedules;

namespace LumaBrake.Cli;

internal static class ScheduleCliParsing
{
    public const string SoftwareFallbackFlag = "--fallback-software";

    public static double ParseManualOpacity(string value)
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

    public static TintOptions ParseTintOptions(IReadOnlyList<string> args)
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

    public static int ParseDayMask(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "all" or "daily" or "everyday" or "every-day" => ScheduleDayMask.EveryDay,
            "weekday" or "weekdays" or "workday" or "workdays" => ScheduleDayMask.Weekdays,
            "weekend" or "weekends" => ScheduleDayMask.Weekend,
            _ => ParseDayList(normalized)
        };
    }

    public static (TintOptions Tint, string? Name) ParseTintAndName(IReadOnlyList<string> args, int startIndex)
    {
        if (args.Count <= startIndex)
        {
            return (new TintOptions { Enabled = false }, null);
        }

        if (!IsTintMode(args[startIndex]))
        {
            return (new TintOptions { Enabled = false }, string.Join(' ', args.Skip(startIndex)));
        }

        if (string.Equals(args[startIndex], "off", StringComparison.OrdinalIgnoreCase))
        {
            return (new TintOptions { Enabled = false }, JoinName(args, startIndex + 1));
        }

        var opacity = 0.12;
        var nameIndex = startIndex + 1;
        if (args.Count > nameIndex && TryParseTintOpacity(args[nameIndex], out var parsedOpacity))
        {
            opacity = parsedOpacity;
            nameIndex++;
        }

        var tint = args[startIndex].ToLowerInvariant() switch
        {
            "warm" => new TintOptions { Enabled = true, Red = 255, Green = 214, Blue = 170, Opacity = opacity },
            "neutral" => new TintOptions { Enabled = true, Red = 255, Green = 244, Blue = 230, Opacity = opacity },
            _ => new TintOptions { Enabled = false }
        };

        return (tint, JoinName(args, nameIndex));
    }

    public static int ParseClockMinute(string value)
    {
        if (!TimeOnly.TryParseExact(value, "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) &&
            !TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
        {
            throw new ArgumentException("Schedule time must use HH:mm format.", nameof(value));
        }

        return (time.Hour * 60) + time.Minute;
    }

    public static HardwareBrightnessTarget ParseHardwareBrightnessTarget(IReadOnlyList<string> args, int startIndex)
    {
        if (args.Count <= startIndex)
        {
            throw new ArgumentException("Hardware brightness target is required.", nameof(args));
        }

        if (string.Equals(args[startIndex], "off", StringComparison.OrdinalIgnoreCase))
        {
            return new HardwareBrightnessTarget { Enabled = false };
        }

        if (args.Count <= startIndex + 2)
        {
            throw new ArgumentException("Hardware brightness target must include display device, physical monitor index, and brightness.", nameof(args));
        }

        var displayDeviceName = args[startIndex].Trim();
        if (string.IsNullOrWhiteSpace(displayDeviceName) || string.Equals(displayDeviceName, "all", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Hardware brightness target must name one display device.", nameof(args));
        }

        if (!int.TryParse(args[startIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var physicalMonitorIndex) ||
            physicalMonitorIndex < 0)
        {
            throw new ArgumentException("Physical monitor index must be 0 or greater.", nameof(args));
        }

        if (!int.TryParse(args[startIndex + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var brightness) ||
            brightness is < 0 or > 100)
        {
            throw new ArgumentException("Hardware brightness must be from 0 to 100.", nameof(args));
        }

        return new HardwareBrightnessTarget
        {
            Enabled = true,
            DisplayDeviceName = displayDeviceName,
            PhysicalMonitorIndex = physicalMonitorIndex,
            Brightness = brightness,
            UseSoftwareFallback = args.Skip(startIndex + 3).Any(arg =>
                string.Equals(arg, SoftwareFallbackFlag, StringComparison.OrdinalIgnoreCase))
        }.Clamp();
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

    private static int ParseDayList(string value)
    {
        var mask = 0;
        foreach (var rawToken in value.Split([',', '+', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            mask |= rawToken switch
            {
                "sun" or "sunday" => ScheduleDayMask.Sunday,
                "mon" or "monday" => ScheduleDayMask.Monday,
                "tue" or "tues" or "tuesday" => ScheduleDayMask.Tuesday,
                "wed" or "wednesday" => ScheduleDayMask.Wednesday,
                "thu" or "thur" or "thurs" or "thursday" => ScheduleDayMask.Thursday,
                "fri" or "friday" => ScheduleDayMask.Friday,
                "sat" or "saturday" => ScheduleDayMask.Saturday,
                _ => throw new ArgumentException("Schedule days must be everyday, weekdays, weekend, or comma-separated day names.")
            };
        }

        if (mask == 0)
        {
            throw new ArgumentException("Schedule days must include at least one day.");
        }

        return ScheduleDayMask.Normalize(mask);
    }

    private static bool TryParseTintOpacity(string value, out double opacity)
    {
        var normalized = value.Trim().TrimEnd('%');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            opacity = 0;
            return false;
        }

        opacity = Math.Clamp(parsed > 1 ? parsed / 100.0 : parsed, 0, 0.60);
        return true;
    }

    private static bool IsTintMode(string value) =>
        string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "warm", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "neutral", StringComparison.OrdinalIgnoreCase);

    private static string? JoinName(IReadOnlyList<string> args, int startIndex) =>
        args.Count <= startIndex ? null : string.Join(' ', args.Skip(startIndex));
}
