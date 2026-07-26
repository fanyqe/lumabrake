using System.Globalization;
using LumaBrake.Domain.Hardware;
using LumaBrake.MonitorControl;
using LumaBrake.Persistence;

namespace LumaBrake.Cli;

internal static class HardwareCliCommands
{
    private const string ConfirmHardwareWriteFlag = "--confirm-hardware-write";

    public static int Run(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count >= 2 && string.Equals(args[1], "list", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var capability in new DdcCiMonitorCatalog().GetCapabilities())
            {
                Console.WriteLine(
                    $"{KeyValue("device", capability.DisplayDeviceName)} " +
                    $"{KeyValue("physical", capability.PhysicalDescription)} " +
                    $"physicalIndex=\"{capability.PhysicalMonitorIndex.ToString(CultureInfo.InvariantCulture)}\" " +
                    $"ddcCi=\"{(capability.SupportsDdcCi ? "yes" : "no")}\" " +
                    $"capabilitiesLength=\"{capability.CapabilitiesLength.ToString(CultureInfo.InvariantCulture)}\" " +
                    $"{KeyValue("error", capability.Error ?? string.Empty)}");
            }

            return 0;
        }

        if (args.Count >= 2 && string.Equals(args[1], "brightness", StringComparison.OrdinalIgnoreCase))
        {
            return RunBrightness(args, store, settings);
        }

        if (args.Count >= 2 && string.Equals(args[1], "writes", StringComparison.OrdinalIgnoreCase))
        {
            return RunWrites(args, store, settings);
        }

        if (args.Count >= 2 && string.Equals(args[1], "blacklist", StringComparison.OrdinalIgnoreCase))
        {
            return RunBlacklist(args, store, settings);
        }

        Console.Error.WriteLine("Usage: lumabrake hardware <list|brightness|writes|blacklist>");
        return 2;
    }

    private static int RunBrightness(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count == 2)
        {
            var options = settings.HardwareBrightness with { Enabled = true };
            foreach (var reading in new DdcCiBrightnessReader().ReadBrightness(options))
            {
                Console.WriteLine(
                    $"{KeyValue("device", reading.DisplayDeviceName)} " +
                    $"{KeyValue("physical", reading.PhysicalDescription)} " +
                    $"physicalIndex=\"{reading.PhysicalMonitorIndex.ToString(CultureInfo.InvariantCulture)}\" " +
                    $"available=\"{(reading.IsAvailable ? "yes" : "no")}\" " +
                    $"minimum=\"{reading.Minimum.ToString(CultureInfo.InvariantCulture)}\" " +
                    $"current=\"{reading.Current.ToString(CultureInfo.InvariantCulture)}\" " +
                    $"maximum=\"{reading.Maximum.ToString(CultureInfo.InvariantCulture)}\" " +
                    $"{KeyValue("error", reading.Error ?? string.Empty)}");
            }

            return 0;
        }

        return args[2].ToLowerInvariant() switch
        {
            "plan" => RunBrightnessPlan(args, store, settings),
            "set" => RunBrightnessSet(args, store, settings),
            _ => PrintBrightnessUsage()
        };
    }

    private static int RunBrightnessPlan(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (!TryParseBrightnessWriteTarget(
                args,
                "Usage: lumabrake hardware brightness plan <display-device> <physical-index> <0-100>",
                out var displayDeviceName,
                out var physicalMonitorIndex,
                out var requestedBrightness))
        {
            return 2;
        }

        var now = DateTimeOffset.Now;
        var targetKey = HardwareBrightnessTargetKey(displayDeviceName, physicalMonitorIndex);
        var lastWriteAt = new HardwareBrightnessStateStore(rootDirectory: store.RootDirectory)
            .Load()
            .LastWriteAttemptAt(targetKey);
        var policy = new HardwareBrightnessPolicy(settings.HardwareBrightness);
        var blockReason = policy.WriteBlockReason(displayDeviceName, now, lastWriteAt);
        var reading = FindBrightnessReading(displayDeviceName, physicalMonitorIndex, settings);
        var resolveReason = reading is null
            ? "Display device or physical monitor index was not found."
            : reading.IsAvailable
                ? null
                : reading.Error;
        var reason = blockReason ?? resolveReason ?? string.Empty;
        var allowed = blockReason is null && reading is { IsAvailable: true };

        Console.WriteLine(
            $"{KeyValue("device", displayDeviceName)} " +
            $"physicalIndex=\"{physicalMonitorIndex.ToString(CultureInfo.InvariantCulture)}\" " +
            $"requested=\"{requestedBrightness.ToString(CultureInfo.InvariantCulture)}\" " +
            $"allowed=\"{(allowed ? "yes" : "no")}\" " +
            $"minimum=\"{(reading?.Minimum.ToString(CultureInfo.InvariantCulture) ?? string.Empty)}\" " +
            $"current=\"{(reading?.Current.ToString(CultureInfo.InvariantCulture) ?? string.Empty)}\" " +
            $"maximum=\"{(reading?.Maximum.ToString(CultureInfo.InvariantCulture) ?? string.Empty)}\" " +
            $"{KeyValue("reason", reason)}");
        return allowed ? 0 : 1;
    }

    private static int RunBrightnessSet(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (!TryParseBrightnessWriteTarget(
                args,
                $"Usage: lumabrake hardware brightness set <display-device> <physical-index> <0-100> {ConfirmHardwareWriteFlag}",
                out var displayDeviceName,
                out var physicalMonitorIndex,
                out var requestedBrightness))
        {
            return 2;
        }

        if (!args.Any(arg => string.Equals(arg, ConfirmHardwareWriteFlag, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine($"Hardware brightness write requires {ConfirmHardwareWriteFlag}.");
            Console.Error.WriteLine($"Usage: lumabrake hardware brightness set <display-device> <physical-index> <0-100> {ConfirmHardwareWriteFlag}");
            return 2;
        }

        var now = DateTimeOffset.Now;
        var targetKey = HardwareBrightnessTargetKey(displayDeviceName, physicalMonitorIndex);
        var stateStore = new HardwareBrightnessStateStore(rootDirectory: store.RootDirectory);
        var lastWriteAt = stateStore.Load().LastWriteAttemptAt(targetKey);
        var results = new DdcCiBrightnessWriter().SetBrightness(
            displayDeviceName,
            physicalMonitorIndex,
            requestedBrightness,
            settings.HardwareBrightness,
            now,
            lastWriteAt);

        if (results.Any(ResultAttemptedNativeWrite))
        {
            stateStore.RecordWriteAttempt(targetKey, now);
        }

        foreach (var result in results)
        {
            Console.WriteLine(
                $"{KeyValue("device", result.DisplayDeviceName)} " +
                $"{KeyValue("physical", result.PhysicalDescription)} " +
                $"physicalIndex=\"{result.PhysicalMonitorIndex.ToString(CultureInfo.InvariantCulture)}\" " +
                $"requested=\"{result.RequestedBrightness.ToString(CultureInfo.InvariantCulture)}\" " +
                $"applied=\"{result.AppliedHardwareBrightness.ToString(CultureInfo.InvariantCulture)}\" " +
                $"written=\"{(result.Succeeded ? "yes" : "no")}\" " +
                $"{KeyValue("error", result.Error ?? string.Empty)}");
        }

        return results.All(result => result.Succeeded) ? 0 : 1;
    }

    private static int RunWrites(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 3)
        {
            Console.Error.WriteLine("Usage: lumabrake hardware writes <status|enable|disable>");
            return 2;
        }

        var options = settings.HardwareBrightness.Clamp();
        switch (args[2].ToLowerInvariant())
        {
            case "status":
                Console.WriteLine($"Hardware brightness control: {(options.Enabled ? "enabled" : "disabled")}");
                Console.WriteLine($"Hardware brightness writes: {(options.AllowWrites ? "enabled" : "disabled")}");
                Console.WriteLine($"Minimum write interval: {options.MinimumWriteInterval.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} seconds");
                return 0;

            case "enable":
                SaveHardwareOptions(store, settings, options with { Enabled = true, AllowWrites = true });
                Console.WriteLine($"Hardware brightness writes enabled. Each write still requires {ConfirmHardwareWriteFlag}.");
                return 0;

            case "disable":
                SaveHardwareOptions(store, settings, options with { Enabled = false, AllowWrites = false });
                Console.WriteLine("Hardware brightness writes disabled.");
                return 0;

            default:
                Console.Error.WriteLine("Usage: lumabrake hardware writes <status|enable|disable>");
                return 2;
        }
    }

    private static int RunBlacklist(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count < 3)
        {
            Console.Error.WriteLine("Usage: lumabrake hardware blacklist <list|add|remove|clear> [display-device]");
            return 2;
        }

        var options = settings.HardwareBrightness.Clamp();
        switch (args[2].ToLowerInvariant())
        {
            case "list":
                foreach (var displayDeviceName in options.BlacklistedDisplayDeviceNames)
                {
                    Console.WriteLine($"{KeyValue("device", displayDeviceName)}");
                }

                return 0;

            case "add":
                if (args.Count < 4)
                {
                    Console.Error.WriteLine("Usage: lumabrake hardware blacklist add <display-device>");
                    return 2;
                }

                SaveHardwareOptions(store, settings, options with
                {
                    BlacklistedDisplayDeviceNames = [.. options.BlacklistedDisplayDeviceNames, args[3]]
                });
                Console.WriteLine("Hardware brightness blacklist updated.");
                return 0;

            case "remove":
                if (args.Count < 4)
                {
                    Console.Error.WriteLine("Usage: lumabrake hardware blacklist remove <display-device>");
                    return 2;
                }

                SaveHardwareOptions(store, settings, options with
                {
                    BlacklistedDisplayDeviceNames = options.BlacklistedDisplayDeviceNames
                        .Where(value => !string.Equals(value, args[3], StringComparison.OrdinalIgnoreCase))
                        .ToArray()
                });
                Console.WriteLine("Hardware brightness blacklist updated.");
                return 0;

            case "clear":
                SaveHardwareOptions(store, settings, options with { BlacklistedDisplayDeviceNames = [] });
                Console.WriteLine("Hardware brightness blacklist cleared.");
                return 0;

            default:
                Console.Error.WriteLine("Usage: lumabrake hardware blacklist <list|add|remove|clear> [display-device]");
                return 2;
        }
    }

    private static void SaveHardwareOptions(SettingsStore store, LumaBrakeSettings settings, HardwareBrightnessOptions options) =>
        store.Save(settings with { HardwareBrightness = options.Clamp() });

    private static bool ResultAttemptedNativeWrite(HardwareBrightnessWriteResult result) =>
        result.Succeeded ||
        result.Error?.StartsWith("Hardware brightness could not be written.", StringComparison.Ordinal) == true;

    private static HardwareBrightnessReading? FindBrightnessReading(
        string displayDeviceName,
        int physicalMonitorIndex,
        LumaBrakeSettings settings) =>
        new DdcCiBrightnessReader()
            .ReadBrightness(settings.HardwareBrightness with { Enabled = true })
            .FirstOrDefault(reading =>
                string.Equals(reading.DisplayDeviceName, displayDeviceName, StringComparison.OrdinalIgnoreCase) &&
                reading.PhysicalMonitorIndex == physicalMonitorIndex);

    private static bool TryParseBrightnessWriteTarget(
        IReadOnlyList<string> args,
        string usage,
        out string displayDeviceName,
        out int physicalMonitorIndex,
        out int requestedBrightness)
    {
        displayDeviceName = string.Empty;
        physicalMonitorIndex = 0;
        requestedBrightness = 0;

        if (args.Count < 6)
        {
            Console.Error.WriteLine(usage);
            return false;
        }

        displayDeviceName = args[3].Trim();
        if (string.IsNullOrWhiteSpace(displayDeviceName) || string.Equals(displayDeviceName, "all", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Display device name must target one display device.");
            Console.Error.WriteLine(usage);
            return false;
        }

        if (!int.TryParse(args[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out physicalMonitorIndex) ||
            physicalMonitorIndex < 0)
        {
            Console.Error.WriteLine("Physical monitor index must be 0 or greater.");
            Console.Error.WriteLine(usage);
            return false;
        }

        if (!int.TryParse(args[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out requestedBrightness) ||
            requestedBrightness is < 0 or > 100)
        {
            Console.Error.WriteLine("Hardware brightness must be from 0 to 100.");
            Console.Error.WriteLine(usage);
            return false;
        }

        return true;
    }

    private static int PrintBrightnessUsage()
    {
        Console.Error.WriteLine($"Usage: lumabrake hardware brightness [plan <display-device> <physical-index> <0-100>|set <display-device> <physical-index> <0-100> {ConfirmHardwareWriteFlag}]");
        return 2;
    }

    private static string HardwareBrightnessTargetKey(string displayDeviceName, int physicalMonitorIndex) =>
        $"{displayDeviceName.Trim()}#{physicalMonitorIndex.ToString(CultureInfo.InvariantCulture)}";

    private static string KeyValue(string key, string value)
    {
        var escaped = value
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        return $"{key}=\"{escaped}\"";
    }
}
