using System.Globalization;
using LumaBrake.MonitorControl;

namespace LumaBrake.Cli;

internal static class HardwareCliCommands
{
    public static int Run(IReadOnlyList<string> args)
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
            foreach (var reading in new DdcCiBrightnessReader().ReadBrightness())
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

        Console.Error.WriteLine("Usage: lumabrake hardware <list|brightness>");
        return 2;
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
