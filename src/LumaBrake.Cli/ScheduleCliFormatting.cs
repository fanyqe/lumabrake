using System.Globalization;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Hardware;
using LumaBrake.Domain.Schedules;

namespace LumaBrake.Cli;

internal static class ScheduleCliFormatting
{
    public static string FormatClock(int minuteOfDay)
    {
        var minute = Math.Clamp(minuteOfDay, 0, 1439);
        return $"{minute / 60:00}:{minute % 60:00}";
    }

    public static string FormatDayMask(int dayMask)
    {
        var normalized = ScheduleDayMask.Normalize(dayMask);
        return normalized switch
        {
            ScheduleDayMask.EveryDay => "everyday",
            ScheduleDayMask.Weekdays => "weekdays",
            ScheduleDayMask.Weekend => "weekend",
            _ => string.Join(',', DayNames(normalized))
        };
    }

    public static string FormatTint(TintOptions tint)
    {
        var effect = tint.ToEffect();
        return effect.Enabled
            ? $"#{effect.Red:X2}{effect.Green:X2}{effect.Blue:X2} {Math.Round(effect.Opacity * 100).ToString("0", CultureInfo.InvariantCulture)}%"
            : "off";
    }

    public static string FormatHardwareBrightness(HardwareBrightnessTarget target)
    {
        var clamped = target.Clamp();
        if (!clamped.Enabled)
        {
            return "off";
        }

        var fallback = clamped.UseSoftwareFallback ? " fallback" : string.Empty;
        return $"{clamped.DisplayDeviceName}#{clamped.PhysicalMonitorIndex.ToString(CultureInfo.InvariantCulture)} {clamped.Brightness.ToString(CultureInfo.InvariantCulture)}%{fallback}";
    }

    public static string KeyValue(string key, string value)
    {
        var escaped = value
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        return $"{key}=\"{escaped}\"";
    }

    private static IEnumerable<string> DayNames(int dayMask)
    {
        if (ScheduleDayMask.Includes(dayMask, DayOfWeek.Sunday))
        {
            yield return "sun";
        }

        if (ScheduleDayMask.Includes(dayMask, DayOfWeek.Monday))
        {
            yield return "mon";
        }

        if (ScheduleDayMask.Includes(dayMask, DayOfWeek.Tuesday))
        {
            yield return "tue";
        }

        if (ScheduleDayMask.Includes(dayMask, DayOfWeek.Wednesday))
        {
            yield return "wed";
        }

        if (ScheduleDayMask.Includes(dayMask, DayOfWeek.Thursday))
        {
            yield return "thu";
        }

        if (ScheduleDayMask.Includes(dayMask, DayOfWeek.Friday))
        {
            yield return "fri";
        }

        if (ScheduleDayMask.Includes(dayMask, DayOfWeek.Saturday))
        {
            yield return "sat";
        }
    }
}
