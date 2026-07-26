using System.Runtime.InteropServices;
using LumaBrake.Domain.Hardware;

namespace LumaBrake.MonitorControl;

public sealed class DdcCiBrightnessReader
{
    public IReadOnlyList<HardwareBrightnessReading> ReadBrightness(HardwareBrightnessOptions? options = null)
    {
        var policy = new HardwareBrightnessPolicy(options ?? new HardwareBrightnessOptions { Enabled = true });
        return DdcCiPhysicalMonitorSession.Enumerate(Failed, Read, policy.IsBlacklisted);
    }

    private static HardwareBrightnessReading Read(string displayDeviceName, PhysicalMonitor physicalMonitor)
    {
        var description = DdcCiPhysicalMonitorSession.DescriptionOrDeviceName(displayDeviceName, physicalMonitor);
        if (!MonitorControlNativeMethods.GetMonitorBrightness(
            physicalMonitor.Handle,
            out var minimum,
            out var current,
            out var maximum))
        {
            return Failed(displayDeviceName, description, $"Hardware brightness could not be read. Win32 error: {Marshal.GetLastWin32Error()}.");
        }

        if (maximum < minimum || current < minimum || current > maximum)
        {
            return Failed(displayDeviceName, description, $"Hardware brightness returned an invalid range: {minimum}/{current}/{maximum}.");
        }

        return new HardwareBrightnessReading(
            displayDeviceName,
            description,
            IsAvailable: true,
            (int)minimum,
            (int)current,
            (int)maximum,
            Error: null);
    }

    private static HardwareBrightnessReading Failed(string displayDeviceName, string physicalDescription, string error) =>
        new(
            displayDeviceName,
            physicalDescription,
            IsAvailable: false,
            Minimum: 0,
            Current: 0,
            Maximum: 0,
            error);
}
