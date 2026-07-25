using System.Globalization;
using LumaBrake.Domain.Rules;
using LumaBrake.Persistence;

namespace LumaBrake.Cli;

internal static class ApplicationRuleCliCommands
{
    private const string Usage = "Usage: lumabrake app-rule <status|list|add|remove|enable|disable|enable-entry|disable-entry|clear>";
    private const string AddUsage = "Usage: lumabrake app-rule add <id> [--process <process-name>] [--title-contains <text>] [--name <name>] [--disabled] [--night-color|--only-night-color]";

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
            "list" => ListRules(settings),
            "add" => AddRule(args, store, settings),
            "remove" => RemoveRule(args, store, settings),
            "enable" => SetRulesEnabled(store, settings, enabled: true),
            "disable" => SetRulesEnabled(store, settings, enabled: false),
            "enable-entry" => SetRuleEnabled(args, store, settings, enabled: true),
            "disable-entry" => SetRuleEnabled(args, store, settings, enabled: false),
            "clear" => ClearRules(store, settings),
            _ => PrintUsage()
        };
    }

    private static int PrintStatus(LumaBrakeSettings settings)
    {
        var options = settings.ApplicationRules.Clamp();
        Console.WriteLine($"Application rules: {(options.Enabled ? "enabled" : "disabled")}");
        Console.WriteLine($"Application rules configured: {options.Entries.Length.ToString(CultureInfo.InvariantCulture)}");
        return 0;
    }

    private static int ListRules(LumaBrakeSettings settings)
    {
        var options = settings.ApplicationRules.Clamp();
        Console.WriteLine($"Application rules: {(options.Enabled ? "enabled" : "disabled")}");
        foreach (var rule in options.Entries)
        {
            Console.WriteLine(
                $"{KeyValue("id", rule.Id)} " +
                $"{KeyValue("name", rule.Name)} " +
                $"enabled=\"{(rule.Enabled ? "yes" : "no")}\" " +
                $"{KeyValue("process", rule.ProcessName ?? string.Empty)} " +
                $"{KeyValue("titleContains", rule.WindowTitleContains ?? string.Empty)} " +
                $"excludeAutomaticEffects=\"{(rule.ExcludeAutomaticEffects ? "yes" : "no")}\" " +
                $"excludeNightColor=\"{(rule.ExcludeNightColor ? "yes" : "no")}\"");
        }

        return 0;
    }

    private static int AddRule(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 3 || string.IsNullOrWhiteSpace(args[2]))
        {
            Console.Error.WriteLine(AddUsage);
            return 2;
        }

        ParsedRuleOptions parsed;
        try
        {
            parsed = ParseRuleOptions(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(AddUsage);
            return 2;
        }

        var id = args[2].Trim();
        var rule = new ApplicationRule
        {
            Id = id,
            Name = parsed.Name ?? id,
            Enabled = parsed.Enabled,
            ProcessName = parsed.ProcessName,
            WindowTitleContains = parsed.WindowTitleContains,
            ExcludeAutomaticEffects = parsed.ExcludeAutomaticEffects,
            ExcludeNightColor = parsed.ExcludeNightColor
        }.Clamp();

        if (string.IsNullOrWhiteSpace(rule.ProcessName) &&
            string.IsNullOrWhiteSpace(rule.WindowTitleContains))
        {
            Console.Error.WriteLine("Application rule requires --process or --title-contains.");
            Console.Error.WriteLine(AddUsage);
            return 2;
        }

        store.Save(settings with
        {
            ApplicationRules = settings.ApplicationRules.Upsert(rule) with { Enabled = true }
        });
        Console.WriteLine($"Application rule saved: {rule.Id}");
        return 0;
    }

    private static int RemoveRule(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (!TryReadRuleId(args, "Usage: lumabrake app-rule remove <id>", out var id))
        {
            return 2;
        }

        if (settings.ApplicationRules.Find(id) is null)
        {
            Console.Error.WriteLine($"Application rule not found: {id}");
            return 1;
        }

        store.Save(settings with { ApplicationRules = settings.ApplicationRules.Remove(id) });
        Console.WriteLine($"Application rule removed: {id}");
        return 0;
    }

    private static int SetRulesEnabled(SettingsStore store, LumaBrakeSettings settings, bool enabled)
    {
        store.Save(settings with { ApplicationRules = settings.ApplicationRules.Clamp() with { Enabled = enabled } });
        Console.WriteLine(enabled ? "Application rules enabled." : "Application rules disabled.");
        return 0;
    }

    private static int SetRuleEnabled(
        IReadOnlyList<string> args,
        SettingsStore store,
        LumaBrakeSettings settings,
        bool enabled)
    {
        if (!TryReadRuleId(
                args,
                enabled
                    ? "Usage: lumabrake app-rule enable-entry <id>"
                    : "Usage: lumabrake app-rule disable-entry <id>",
                out var id))
        {
            return 2;
        }

        var rule = settings.ApplicationRules.Find(id);
        if (rule is null)
        {
            Console.Error.WriteLine($"Application rule not found: {id}");
            return 1;
        }

        store.Save(settings with { ApplicationRules = settings.ApplicationRules.SetEntryEnabled(id, enabled) });
        Console.WriteLine($"Application rule {(enabled ? "enabled" : "disabled")}: {rule.Id}");
        return 0;
    }

    private static int ClearRules(SettingsStore store, LumaBrakeSettings settings)
    {
        store.Save(settings with { ApplicationRules = settings.ApplicationRules with { Enabled = false, Entries = [] } });
        Console.WriteLine("Application rules cleared.");
        return 0;
    }

    private static ParsedRuleOptions ParseRuleOptions(IReadOnlyList<string> args)
    {
        string? name = null;
        string? processName = null;
        string? windowTitleContains = null;
        var enabled = true;
        var excludeAutomaticEffects = true;
        var excludeNightColor = false;

        for (var index = 3; index < args.Count; index++)
        {
            switch (args[index].ToLowerInvariant())
            {
                case "--name":
                    name = ReadOptionValue(args, ref index, "--name");
                    break;
                case "--process":
                    processName = ReadOptionValue(args, ref index, "--process");
                    break;
                case "--title-contains":
                    windowTitleContains = ReadOptionValue(args, ref index, "--title-contains");
                    break;
                case "--disabled":
                    enabled = false;
                    break;
                case "--night-color":
                    excludeNightColor = true;
                    break;
                case "--only-night-color":
                    excludeAutomaticEffects = false;
                    excludeNightColor = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown application rule option: {args[index]}");
            }
        }

        return new ParsedRuleOptions(
            Normalize(name),
            Normalize(processName),
            Normalize(windowTitleContains),
            enabled,
            excludeAutomaticEffects,
            excludeNightColor);
    }

    private static string ReadOptionValue(IReadOnlyList<string> args, ref int index, string optionName)
    {
        if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new ArgumentException($"{optionName} requires a value.");
        }

        index++;
        return args[index].Trim();
    }

    private static bool TryReadRuleId(IReadOnlyList<string> args, string usage, out string id)
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
        Console.Error.WriteLine(Usage);
        return 2;
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string KeyValue(string key, string value)
    {
        var escaped = value
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        return $"{key}=\"{escaped}\"";
    }

    private sealed record ParsedRuleOptions(
        string? Name,
        string? ProcessName,
        string? WindowTitleContains,
        bool Enabled,
        bool ExcludeAutomaticEffects,
        bool ExcludeNightColor);
}
