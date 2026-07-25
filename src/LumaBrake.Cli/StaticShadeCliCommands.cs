using System.Globalization;
using LumaBrake.Domain.Analysis;
using LumaBrake.Persistence;

namespace LumaBrake.Cli;

internal static class StaticShadeCliCommands
{
    private static readonly (string Name, StaticShadePreset Preset, string Description)[] Presets =
    [
        ("oled-balanced", StaticShadePreset.OledBalanced, "balanced OLED/static UI protection"),
        ("oled-conservative", StaticShadePreset.OledConservative, "earlier OLED protection with moderate opacity"),
        ("oled-aggressive", StaticShadePreset.OledAggressive, "earliest OLED protection with stronger opacity"),
        ("static-hud", StaticShadePreset.StaticHud, "small persistent HUD and overlay elements"),
        ("office", StaticShadePreset.OfficeUse, "gentler protection for bright documents and office apps"),
        ("disabled", StaticShadePreset.Disabled, "turn StaticShade off")
    ];

    public static int Run(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 2)
        {
            return PrintUsage();
        }

        return args[1].ToLowerInvariant() switch
        {
            "status" => PrintStatus(settings.StaticShade),
            "list" => ListPresets(),
            "preset" => ApplyPreset(args, store, settings),
            _ => PrintUsage()
        };
    }

    private static int PrintStatus(StaticShadeOptions options)
    {
        Console.WriteLine($"StaticShade: {(options.Enabled ? "enabled" : "disabled")}");
        Console.WriteLine($"Minimum static duration: {options.MinimumStaticDuration}");
        Console.WriteLine($"Minimum exposure score: {options.MinimumExposureScore.ToString("0.00", CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Maximum opacity: {Math.Round(options.MaximumOpacity * 100).ToString("0", CultureInfo.InvariantCulture)}%");
        return 0;
    }

    private static int ListPresets()
    {
        foreach (var preset in Presets)
        {
            Console.WriteLine($"{preset.Name}: {preset.Description}");
        }

        return 0;
    }

    private static int ApplyPreset(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 3 || !TryParsePreset(args[2], out var preset, out var presetName))
        {
            Console.Error.WriteLine("Usage: lumabrake static-shade preset <oled-balanced|oled-conservative|oled-aggressive|static-hud|office|disabled>");
            return 2;
        }

        var options = StaticShadeOptions.ForPreset(preset);
        store.Save(ProtectionTuningPolicy.Apply(settings with
        {
            StaticShade = options,
            ProtectionTuning = settings.ProtectionTuning.Clamp() with { StaticShadeProfile = preset }
        }));
        Console.WriteLine($"StaticShade preset applied: {presetName}");
        return 0;
    }

    private static bool TryParsePreset(string value, out StaticShadePreset preset, out string presetName)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (string.Equals(normalized, "balanced", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "oled-balanced";
        }

        foreach (var candidate in Presets)
        {
            if (string.Equals(candidate.Name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                preset = candidate.Preset;
                presetName = candidate.Name;
                return true;
            }
        }

        preset = StaticShadePreset.OledBalanced;
        presetName = string.Empty;
        return false;
    }

    private static int PrintUsage()
    {
        Console.Error.WriteLine("Usage: lumabrake static-shade <status|list|preset>");
        return 2;
    }
}
