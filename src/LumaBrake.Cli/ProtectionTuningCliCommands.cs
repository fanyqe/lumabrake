using System.Globalization;
using LumaBrake.Domain.Analysis;
using LumaBrake.Persistence;

namespace LumaBrake.Cli;

internal static class ProtectionTuningCliCommands
{
    private const string Usage =
        "Usage: lumabrake tuning <status|list|preset|strength|response|local|mask-fit|white-page|static>";

    public static int Run(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 2)
        {
            return PrintUsage();
        }

        return args[1].ToLowerInvariant() switch
        {
            "status" => PrintStatus(settings),
            "list" => ListChoices(),
            "preset" => ApplyPreset(args, store, settings),
            "strength" => ApplyStrength(args, store, settings),
            "response" => ApplyResponse(args, store, settings),
            "local" => ApplyLocal(args, store, settings),
            "mask-fit" => ApplyMaskFit(args, store, settings),
            "white-page" => ApplyWhitePage(args, store, settings),
            "static" => ApplyStaticShade(args, store, settings),
            _ => PrintUsage()
        };
    }

    private static int PrintStatus(LumaBrakeSettings settings)
    {
        var tuning = settings.ProtectionTuning.Clamp();
        Console.WriteLine($"Protection strength: {FormatStrength(tuning.Strength)}");
        Console.WriteLine($"Response speed: {FormatResponse(tuning.Response)}");
        Console.WriteLine($"Local masks: {FormatLocal(tuning.LocalProtection)}");
        Console.WriteLine($"Local mask fit: {FormatMaskFit(tuning.LocalMaskFit)}");
        Console.WriteLine($"White pages: {FormatWhitePage(tuning.WhitePageProtection)}");
        Console.WriteLine($"Static surface profile: {FormatStaticShade(tuning.StaticShadeProfile)}");
        Console.WriteLine($"Maximum dimming: {Math.Round(settings.UserMaximumOpacity * 100).ToString("0", CultureInfo.InvariantCulture)}%");
        Console.WriteLine($"StaticShade maximum opacity: {Math.Round(settings.StaticShade.MaximumOpacity * 100).ToString("0", CultureInfo.InvariantCulture)}%");
        Console.WriteLine($"Adaptive maximum opacity: {Math.Round(settings.AdaptiveContent.MaximumOpacity * 100).ToString("0", CultureInfo.InvariantCulture)}%");
        return 0;
    }

    private static int ListChoices()
    {
        Console.WriteLine("preset: comfortable|balanced|strong|maximum");
        Console.WriteLine("strength: gentle|balanced|strong|maximum");
        Console.WriteLine("response: calm|balanced|fast");
        Console.WriteLine("local: calm|balanced|strong|aggressive");
        Console.WriteLine("mask-fit: precise|balanced|expanded");
        Console.WriteLine("white-page: light|normal|strong");
        Console.WriteLine("static: balanced|oled-gentle|oled-strong|small-ui|office|disabled");
        return 0;
    }

    private static int ApplyPreset(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 3)
        {
            Console.Error.WriteLine("Usage: lumabrake tuning preset <comfortable|balanced|strong|maximum>");
            return 2;
        }

        var preset = args[2].Trim().ToLowerInvariant();
        var tuning = settings.ProtectionTuning.Clamp();
        switch (preset)
        {
            case "comfortable":
                tuning = tuning with
                {
                    Strength = ProtectionStrengthPreset.Gentle,
                    Response = ProtectionResponsePreset.Calm,
                    LocalProtection = LocalProtectionPreset.Calm,
                    LocalMaskFit = LocalMaskFitPreset.Precise,
                    WhitePageProtection = WhitePageProtectionPreset.Light,
                    StaticShadeProfile = StaticShadePreset.OfficeUse
                };
                break;
            case "balanced":
                tuning = new ProtectionTuningOptions();
                break;
            case "strong":
                tuning = tuning with
                {
                    Strength = ProtectionStrengthPreset.Strong,
                    Response = ProtectionResponsePreset.Fast,
                    LocalProtection = LocalProtectionPreset.Strong,
                    LocalMaskFit = LocalMaskFitPreset.Balanced,
                    WhitePageProtection = WhitePageProtectionPreset.Strong,
                    StaticShadeProfile = StaticShadePreset.StaticHud
                };
                break;
            case "maximum":
                tuning = tuning with
                {
                    Strength = ProtectionStrengthPreset.Maximum,
                    Response = ProtectionResponsePreset.Fast,
                    LocalProtection = LocalProtectionPreset.Aggressive,
                    LocalMaskFit = LocalMaskFitPreset.Balanced,
                    WhitePageProtection = WhitePageProtectionPreset.Strong,
                    StaticShadeProfile = StaticShadePreset.OledAggressive
                };
                break;
            default:
                Console.Error.WriteLine("Usage: lumabrake tuning preset <comfortable|balanced|strong|maximum>");
                return 2;
        }

        SaveTuning(store, settings with
        {
            StaticShade = settings.StaticShade with { Enabled = tuning.StaticShadeProfile != StaticShadePreset.Disabled }
        }, tuning);
        Console.WriteLine($"Protection tuning preset applied: {preset}");
        return 0;
    }

    private static int ApplyStrength(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 3 || !TryParseStrength(args[2], out var strength))
        {
            Console.Error.WriteLine("Usage: lumabrake tuning strength <gentle|balanced|strong|maximum>");
            return 2;
        }

        SaveTuning(store, settings, settings.ProtectionTuning.Clamp() with { Strength = strength });
        Console.WriteLine($"Protection strength: {FormatStrength(strength)}");
        return 0;
    }

    private static int ApplyResponse(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 3 || !TryParseResponse(args[2], out var response))
        {
            Console.Error.WriteLine("Usage: lumabrake tuning response <calm|balanced|fast>");
            return 2;
        }

        SaveTuning(store, settings, settings.ProtectionTuning.Clamp() with { Response = response });
        Console.WriteLine($"Response speed: {FormatResponse(response)}");
        return 0;
    }

    private static int ApplyLocal(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 3 || !TryParseLocal(args[2], out var local))
        {
            Console.Error.WriteLine("Usage: lumabrake tuning local <calm|balanced|strong|aggressive>");
            return 2;
        }

        SaveTuning(store, settings, settings.ProtectionTuning.Clamp() with { LocalProtection = local });
        Console.WriteLine($"Local masks: {FormatLocal(local)}");
        return 0;
    }

    private static int ApplyWhitePage(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 3 || !TryParseWhitePage(args[2], out var whitePage))
        {
            Console.Error.WriteLine("Usage: lumabrake tuning white-page <light|normal|strong>");
            return 2;
        }

        SaveTuning(store, settings, settings.ProtectionTuning.Clamp() with { WhitePageProtection = whitePage });
        Console.WriteLine($"White pages: {FormatWhitePage(whitePage)}");
        return 0;
    }

    private static int ApplyMaskFit(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 3 || !TryParseMaskFit(args[2], out var maskFit))
        {
            Console.Error.WriteLine("Usage: lumabrake tuning mask-fit <precise|balanced|expanded>");
            return 2;
        }

        SaveTuning(store, settings, settings.ProtectionTuning.Clamp() with { LocalMaskFit = maskFit });
        Console.WriteLine($"Local mask fit: {FormatMaskFit(maskFit)}");
        return 0;
    }

    private static int ApplyStaticShade(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 3 || !TryParseStaticShade(args[2], out var staticShade))
        {
            Console.Error.WriteLine("Usage: lumabrake tuning static <balanced|oled-gentle|oled-strong|small-ui|office|disabled>");
            return 2;
        }

        var updated = settings with
        {
            StaticShade = settings.StaticShade with { Enabled = staticShade != StaticShadePreset.Disabled },
            ProtectionTuning = settings.ProtectionTuning.Clamp() with { StaticShadeProfile = staticShade }
        };
        store.Save(ProtectionTuningPolicy.Apply(updated));
        Console.WriteLine($"Static surface profile: {FormatStaticShade(staticShade)}");
        return 0;
    }

    private static void SaveTuning(SettingsStore store, LumaBrakeSettings settings, ProtectionTuningOptions tuning) =>
        store.Save(ProtectionTuningPolicy.Apply(settings with { ProtectionTuning = tuning }));

    private static bool TryParseStrength(string value, out ProtectionStrengthPreset strength)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "gentle":
            case "soft":
            case "light":
                strength = ProtectionStrengthPreset.Gentle;
                return true;
            case "balanced":
            case "normal":
                strength = ProtectionStrengthPreset.Balanced;
                return true;
            case "strong":
                strength = ProtectionStrengthPreset.Strong;
                return true;
            case "maximum":
            case "max":
                strength = ProtectionStrengthPreset.Maximum;
                return true;
            default:
                strength = ProtectionStrengthPreset.Balanced;
                return false;
        }
    }

    private static bool TryParseResponse(string value, out ProtectionResponsePreset response)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "calm":
            case "slow":
                response = ProtectionResponsePreset.Calm;
                return true;
            case "balanced":
            case "normal":
                response = ProtectionResponsePreset.Balanced;
                return true;
            case "fast":
                response = ProtectionResponsePreset.Fast;
                return true;
            default:
                response = ProtectionResponsePreset.Balanced;
                return false;
        }
    }

    private static bool TryParseLocal(string value, out LocalProtectionPreset local)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "calm":
            case "light":
                local = LocalProtectionPreset.Calm;
                return true;
            case "balanced":
            case "normal":
                local = LocalProtectionPreset.Balanced;
                return true;
            case "strong":
                local = LocalProtectionPreset.Strong;
                return true;
            case "aggressive":
            case "max":
                local = LocalProtectionPreset.Aggressive;
                return true;
            default:
                local = LocalProtectionPreset.Balanced;
                return false;
        }
    }

    private static bool TryParseWhitePage(string value, out WhitePageProtectionPreset whitePage)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "light":
            case "gentle":
                whitePage = WhitePageProtectionPreset.Light;
                return true;
            case "normal":
            case "balanced":
                whitePage = WhitePageProtectionPreset.Normal;
                return true;
            case "strong":
                whitePage = WhitePageProtectionPreset.Strong;
                return true;
            default:
                whitePage = WhitePageProtectionPreset.Normal;
                return false;
        }
    }

    private static bool TryParseMaskFit(string value, out LocalMaskFitPreset maskFit)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "precise":
            case "tight":
                maskFit = LocalMaskFitPreset.Precise;
                return true;
            case "balanced":
            case "normal":
                maskFit = LocalMaskFitPreset.Balanced;
                return true;
            case "expanded":
            case "wide":
                maskFit = LocalMaskFitPreset.Expanded;
                return true;
            default:
                maskFit = LocalMaskFitPreset.Precise;
                return false;
        }
    }

    private static bool TryParseStaticShade(string value, out StaticShadePreset preset)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "balanced":
            case "oled-balanced":
                preset = StaticShadePreset.OledBalanced;
                return true;
            case "oled-gentle":
            case "oled-conservative":
                preset = StaticShadePreset.OledConservative;
                return true;
            case "oled-strong":
            case "oled-aggressive":
                preset = StaticShadePreset.OledAggressive;
                return true;
            case "small-ui":
            case "static-hud":
                preset = StaticShadePreset.StaticHud;
                return true;
            case "office":
            case "reading":
                preset = StaticShadePreset.OfficeUse;
                return true;
            case "disabled":
            case "off":
                preset = StaticShadePreset.Disabled;
                return true;
            default:
                preset = StaticShadePreset.OledBalanced;
                return false;
        }
    }

    private static string FormatStrength(ProtectionStrengthPreset strength) =>
        strength.ToString().ToLowerInvariant();

    private static string FormatResponse(ProtectionResponsePreset response) =>
        response.ToString().ToLowerInvariant();

    private static string FormatLocal(LocalProtectionPreset local) =>
        local.ToString().ToLowerInvariant();

    private static string FormatMaskFit(LocalMaskFitPreset maskFit) =>
        maskFit.ToString().ToLowerInvariant();

    private static string FormatWhitePage(WhitePageProtectionPreset whitePage) =>
        whitePage.ToString().ToLowerInvariant();

    private static string FormatStaticShade(StaticShadePreset preset) =>
        preset switch
        {
            StaticShadePreset.OledConservative => "oled-gentle",
            StaticShadePreset.OledAggressive => "oled-strong",
            StaticShadePreset.StaticHud => "small-ui",
            StaticShadePreset.OfficeUse => "office",
            StaticShadePreset.Disabled => "disabled",
            _ => "balanced"
        };

    private static int PrintUsage()
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
}
