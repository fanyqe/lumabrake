using System.Globalization;
using LumaBrake.Domain.Color;
using LumaBrake.Persistence;

namespace LumaBrake.Cli;

internal static class NightColorCliCommands
{
    private const string Usage = "Usage: lumabrake night-color <status|off|manual|custom|sun|pause|resume|fullscreen|strength>";

    public static int Run(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 2)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        return args[1].ToLowerInvariant() switch
        {
            "status" => PrintStatus(settings),
            "off" => Save(store, settings, settings.NightColor.Clamp() with { Enabled = false, PausedUntilUtc = null }, "Night color disabled."),
            "manual" => SetManual(args, store, settings),
            "custom" => SetCustom(args, store, settings),
            "sun" => SetSunriseSunset(args, store, settings),
            "pause" => Pause(args, store, settings),
            "resume" => Save(store, settings, settings.NightColor.Clamp() with { PausedUntilUtc = null }, "Night color resumed."),
            "fullscreen" => SetFullScreenPause(args, store, settings),
            "strength" => SetTintStrength(args, store, settings),
            _ => PrintUsage()
        };
    }

    private static int PrintStatus(LumaBrakeSettings settings)
    {
        var options = settings.NightColor.Clamp();
        var state = NightColorEngine.Evaluate(options, DateTimeOffset.Now);
        Console.WriteLine($"Night color: {(options.Enabled ? "enabled" : "disabled")}");
        Console.WriteLine($"Night color mode: {options.ScheduleMode.ToString().ToLowerInvariant()}");
        Console.WriteLine($"Night color temperature: {state.TemperatureKelvin.ToString(CultureInfo.InvariantCulture)}K");
        Console.WriteLine($"Night color brightness: {Math.Round(options.Brightness * 100).ToString("0", CultureInfo.InvariantCulture)}%");
        Console.WriteLine($"Night color strength: {Math.Round(options.TintStrength * 100).ToString("0", CultureInfo.InvariantCulture)}%");
        Console.WriteLine($"Night color location: {options.Latitude.ToString("0.####", CultureInfo.InvariantCulture)}, {options.Longitude.ToString("0.####", CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Night color tint: {FormatTint(state)}");
        Console.WriteLine($"Night color progress: {Math.Round(state.NightProgress * 100).ToString("0", CultureInfo.InvariantCulture)}%");
        Console.WriteLine(options.PausedUntilUtc is { } pausedUntil && pausedUntil > DateTimeOffset.UtcNow
            ? $"Night color paused until UTC: {pausedUntil:O}"
            : "Night color pause: off");
        Console.WriteLine($"Night color full-screen pause: {FormatOnOff(options.PauseInFullScreenApplications)}");
        return 0;
    }

    private static int SetManual(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 3)
        {
            Console.Error.WriteLine("Usage: lumabrake night-color manual <1000-12000K> [20-100]");
            return 2;
        }

        try
        {
            var options = settings.NightColor.Clamp() with
            {
                Enabled = true,
                ScheduleMode = NightColorScheduleMode.Manual,
                ManualTemperatureKelvin = ParseKelvin(args[2]),
                Brightness = args.Count >= 4 ? ParseBrightness(args[3]) : settings.NightColor.Clamp().Brightness,
                PausedUntilUtc = null
            };
            return Save(store, settings, options, $"Night color manual temperature: {options.ManualTemperatureKelvin.ToString(CultureInfo.InvariantCulture)}K");
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Usage: lumabrake night-color manual <1000-12000K> [20-100]");
            return 2;
        }
    }

    private static int SetCustom(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 6)
        {
            Console.Error.WriteLine("Usage: lumabrake night-color custom <start HH:mm> <end HH:mm> <day-K> <night-K> [20-100] [instant|smooth] [transition-minutes]");
            return 2;
        }

        try
        {
            var current = settings.NightColor.Clamp();
            var options = current with
            {
                Enabled = true,
                ScheduleMode = NightColorScheduleMode.Custom,
                CustomStartMinuteOfDay = ScheduleCliParsing.ParseClockMinute(args[2]),
                CustomEndMinuteOfDay = ScheduleCliParsing.ParseClockMinute(args[3]),
                DayTemperatureKelvin = ParseKelvin(args[4]),
                NightTemperatureKelvin = ParseKelvin(args[5]),
                Brightness = args.Count >= 7 ? ParseBrightness(args[6]) : current.Brightness,
                TransitionMode = args.Count >= 8 ? ParseTransitionMode(args[7]) : current.TransitionMode,
                TransitionDuration = args.Count >= 9 ? ParseTransitionDuration(args[8]) : current.TransitionDuration,
                PausedUntilUtc = null
            };
            return Save(store, settings, options, "Night color custom schedule saved.");
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Usage: lumabrake night-color custom <start HH:mm> <end HH:mm> <day-K> <night-K> [20-100] [instant|smooth] [transition-minutes]");
            return 2;
        }
    }

    private static int SetSunriseSunset(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 6)
        {
            Console.Error.WriteLine("Usage: lumabrake night-color sun <latitude> <longitude> <day-K> <night-K> [20-100] [instant|smooth] [transition-minutes]");
            return 2;
        }

        try
        {
            var current = settings.NightColor.Clamp();
            var options = current with
            {
                Enabled = true,
                ScheduleMode = NightColorScheduleMode.SunriseSunset,
                Latitude = ParseDouble(args[2], "Latitude"),
                Longitude = ParseDouble(args[3], "Longitude"),
                DayTemperatureKelvin = ParseKelvin(args[4]),
                NightTemperatureKelvin = ParseKelvin(args[5]),
                Brightness = args.Count >= 7 ? ParseBrightness(args[6]) : current.Brightness,
                TransitionMode = args.Count >= 8 ? ParseTransitionMode(args[7]) : current.TransitionMode,
                TransitionDuration = args.Count >= 9 ? ParseTransitionDuration(args[8]) : current.TransitionDuration,
                PausedUntilUtc = null
            };
            return Save(store, settings, options, "Night color sunrise/sunset schedule saved.");
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Usage: lumabrake night-color sun <latitude> <longitude> <day-K> <night-K> [20-100] [instant|smooth] [transition-minutes]");
            return 2;
        }
    }

    private static int Pause(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 3)
        {
            Console.Error.WriteLine("Usage: lumabrake night-color pause <minutes>");
            return 2;
        }

        try
        {
            var minutes = ParseDouble(args[2], "Pause duration");
            var pausedUntil = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(minutes, 1, 1440));
            return Save(
                store,
                settings,
                settings.NightColor.Clamp() with { PausedUntilUtc = pausedUntil },
                $"Night color paused until UTC: {pausedUntil:O}");
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Usage: lumabrake night-color pause <minutes>");
            return 2;
        }
    }

    private static int SetFullScreenPause(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 3 || !TryParseOnOff(args[2], out var enabled))
        {
            Console.Error.WriteLine("Usage: lumabrake night-color fullscreen <on|off>");
            return 2;
        }

        return Save(
            store,
            settings,
            settings.NightColor.Clamp() with { PauseInFullScreenApplications = enabled },
            $"Night color full-screen pause: {FormatOnOff(enabled)}");
    }

    private static int SetTintStrength(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 3)
        {
            Console.Error.WriteLine("Usage: lumabrake night-color strength <0-100>");
            return 2;
        }

        try
        {
            var strength = ParseUnitPercent(args[2], "Color strength", 0, 100);
            return Save(
                store,
                settings,
                settings.NightColor.Clamp() with { TintStrength = strength },
                $"Night color strength: {Math.Round(strength * 100).ToString("0", CultureInfo.InvariantCulture)}%");
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Usage: lumabrake night-color strength <0-100>");
            return 2;
        }
    }

    private static int Save(SettingsStore store, LumaBrakeSettings settings, NightColorOptions options, string message)
    {
        store.Save(settings with { NightColor = options.Clamp() });
        Console.WriteLine(message);
        return 0;
    }

    private static int PrintUsage()
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }

    private static int ParseKelvin(string value)
    {
        var normalized = value.Trim().TrimEnd('K', 'k');
        if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kelvin))
        {
            throw new ArgumentException("Temperature must be a Kelvin value from 1000 to 12000.");
        }

        return Math.Clamp(kelvin, 1000, 12000);
    }

    private static double ParseBrightness(string value)
    {
        return ParseUnitPercent(value, "Brightness", 20, 100);
    }

    private static double ParseUnitPercent(string value, string name, double minimumPercent, double maximumPercent)
    {
        var normalized = value.Trim().TrimEnd('%');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"{name} must be a percentage from {minimumPercent.ToString("0", CultureInfo.InvariantCulture)} to {maximumPercent.ToString("0", CultureInfo.InvariantCulture)}.");
        }

        var ratio = parsed > 1 ? parsed / 100.0 : parsed;
        return Math.Clamp(ratio, minimumPercent / 100.0, maximumPercent / 100.0);
    }

    private static NightColorTransitionMode ParseTransitionMode(string value) =>
        value.ToLowerInvariant() switch
        {
            "instant" => NightColorTransitionMode.Instant,
            "smooth" => NightColorTransitionMode.Smooth,
            _ => throw new ArgumentException("Transition mode must be instant or smooth.")
        };

    private static TimeSpan ParseTransitionDuration(string value)
    {
        if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes))
        {
            throw new ArgumentException("Transition duration must be a minute value from 0 to 360.");
        }

        return TimeSpan.FromMinutes(Math.Clamp(minutes, 0, 360));
    }

    private static double ParseDouble(string value, string name)
    {
        if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"{name} must be numeric.");
        }

        return parsed;
    }

    private static bool TryParseOnOff(string value, out bool enabled)
    {
        enabled = false;
        switch (value.Trim().ToLowerInvariant())
        {
            case "on":
            case "enable":
            case "enabled":
            case "true":
                enabled = true;
                return true;
            case "off":
            case "disable":
            case "disabled":
            case "false":
                return true;
            default:
                return false;
        }
    }

    private static string FormatTint(NightColorState state) =>
        state.Tint.Enabled
            ? $"#{state.Tint.Red:X2}{state.Tint.Green:X2}{state.Tint.Blue:X2} {Math.Round(state.Tint.Opacity * 100).ToString("0", CultureInfo.InvariantCulture)}%"
            : "off";

    private static string FormatOnOff(bool enabled) => enabled ? "on" : "off";
}
