using System.Globalization;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Profiles;
using LumaBrake.Persistence;

namespace LumaBrake.Cli;

internal static class ProfileCliCommands
{
    public static int Run(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 2)
        {
            Console.Error.WriteLine("Usage: lumabrake profile <list|save|apply|remove>");
            return 2;
        }

        return args[1].ToLowerInvariant() switch
        {
            "list" => ListProfiles(settings),
            "save" => SaveProfile(args, store, settings),
            "apply" => ApplyProfile(args, store, settings),
            "remove" => RemoveProfile(args, store, settings),
            _ => PrintUsage()
        };
    }

    private static int ListProfiles(LumaBrakeSettings settings)
    {
        foreach (var profile in settings.Profiles.Clamp().Entries)
        {
            Console.WriteLine(
                $"{KeyValue("id", profile.Id)} " +
                $"{KeyValue("name", profile.Name)} " +
                $"maximumDimming=\"{Math.Round(profile.UserMaximumOpacity * 100).ToString("0", CultureInfo.InvariantCulture)}\" " +
                $"manualDimming=\"{Math.Round(profile.ManualDimmingOpacity * 100).ToString("0", CultureInfo.InvariantCulture)}\" " +
                $"{KeyValue("tint", FormatTint(profile.Tint))} " +
                $"{KeyValue("focus", FormatFocus(profile))}");
        }

        return 0;
    }

    private static int SaveProfile(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (!TryReadProfileId(args, "Usage: lumabrake profile save <id> [name]", out var id))
        {
            return 2;
        }

        var name = args.Count >= 4 ? string.Join(' ', args.Skip(3)) : id;
        var profile = ProfileSettingsPolicy.Capture(id, name, settings);
        store.Save(settings with { Profiles = settings.Profiles.Upsert(profile) });
        Console.WriteLine($"Profile saved: {profile.Id}");
        return 0;
    }

    private static int ApplyProfile(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (!TryReadProfileId(args, "Usage: lumabrake profile apply <id>", out var id))
        {
            return 2;
        }

        var profile = settings.Profiles.Find(id);
        if (profile is null)
        {
            Console.Error.WriteLine($"Profile not found: {id}");
            return 1;
        }

        store.Save(ProfileSettingsPolicy.Apply(settings, profile));
        Console.WriteLine($"Profile applied: {profile.Id}");
        return 0;
    }

    private static int RemoveProfile(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (!TryReadProfileId(args, "Usage: lumabrake profile remove <id>", out var id))
        {
            return 2;
        }

        if (settings.Profiles.Find(id) is null)
        {
            Console.Error.WriteLine($"Profile not found: {id}");
            return 1;
        }

        store.Save(settings with { Profiles = settings.Profiles.Remove(id) });
        Console.WriteLine($"Profile removed: {id}");
        return 0;
    }

    private static bool TryReadProfileId(IReadOnlyList<string> args, string usage, out string id)
    {
        id = string.Empty;
        if (args.Count < 3 || string.IsNullOrWhiteSpace(args[2]))
        {
            Console.Error.WriteLine(usage);
            return false;
        }

        id = args[2].Trim();
        return true;
    }

    private static int PrintUsage()
    {
        Console.Error.WriteLine("Usage: lumabrake profile <list|save|apply|remove>");
        return 2;
    }

    private static string FormatTint(TintOptions tint)
    {
        var effect = tint.ToEffect();
        return effect.Enabled
            ? $"#{effect.Red:X2}{effect.Green:X2}{effect.Blue:X2} {Math.Round(effect.Opacity * 100).ToString("0", CultureInfo.InvariantCulture)}%"
            : "off";
    }

    private static string FormatFocus(LumaBrakeProfile profile)
    {
        var focus = profile.FocusDimming.Clamp();
        return $"{(focus.Enabled ? "enabled" : "disabled")} {Math.Round(focus.Opacity * 100).ToString("0", CultureInfo.InvariantCulture)}%";
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
