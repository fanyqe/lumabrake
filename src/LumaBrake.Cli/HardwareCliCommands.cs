using System.Globalization;
using LumaBrake.Domain.Hardware;
using LumaBrake.MonitorControl;
using LumaBrake.Persistence;

namespace LumaBrake.Cli;

internal static class HardwareCliCommands
{
    public static int Run(IReadOnlyList<string> args, SettingsStore store, LumaBrakeSettings settings)
    {
        if (args.Count >= 2 && string.Equals(args[1], "list", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var capability in new DdcCiMonitorCatalog().GetCapabilities())
            {
                Console.WriteLine(
                    $"{KeyValue("device", capability.DisplayDeviceName)} " +
                    $"{KeyValue("physical", capability.PhysicalDescription)} " +
                    $"ddcCi=\"{(capability.SupportsDdcCi ? "yes" : "no")}\" " +
                    $"capabilitiesLength=\"{capability.CapabilitiesLength.ToString(CultureInfo.InvariantCulture)}\" " +
                    $"{KeyValue("error", capability.Error ?? string.Empty)}");
            }

            return 0;
        }

        if (args.Count >= 2 && string.Equals(args[1], "brightness", StringComparison.OrdinalIgnoreCase))
        {
            var options = settings.HardwareBrightness with { Enabled = true };
            foreach (var reading in new DdcCiBrightnessReader().ReadBrightness(options))
            {
                Console.WriteLine(
                    $"{KeyValue("device", reading.DisplayDeviceName)} " +
                    $"{KeyValue("physical", reading.PhysicalDescription)} " +
                    $"available=\"{(reading.IsAvailable ? "yes" : "no")}\" " +
                    $"minimum=\"{reading.Minimum.ToString(CultureInfo.InvariantCulture)}\" " +
                    $"current=\"{reading.Current.ToString(CultureInfo.InvariantCulture)}\" " +
                    $"maximum=\"{reading.Maximum.ToString(CultureInfo.InvariantCulture)}\" " +
                    $"{KeyValue("error", reading.Error ?? string.Empty)}");
            }

            return 0;
        }

        if (args.Count >= 2 && string.Equals(args[1], "blacklist", StringComparison.OrdinalIgnoreCase))
        {
            return RunBlacklist(args, store, settings);
        }

        Console.Error.WriteLine("Usage: lumabrake hardware <list|brightness|blacklist>");
        return 2;
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

    private static string KeyValue(string key, string value)
    {
        var escaped = value
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        return $"{key}=\"{escaped}\"";
    }
}
