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

    private static HardwareBrightnessReading Read(string displayDeviceName, PhysicalMonitor physicalMonitor, int physicalMonitorIndex)
    {
        var description = DdcCiPhysicalMonitorSession.DescriptionOrDeviceName(displayDeviceName, physicalMonitor);
        if (!MonitorControlNativeMethods.GetMonitorBrightness(
            physicalMonitor.Handle,
            out var minimum,
            out var current,
            out var maximum))
        {
            return Failed(displayDeviceName, description, physicalMonitorIndex, $"Hardware brightness could not be read. Win32 error: {Marshal.GetLastWin32Error()}.");
        }

        if (maximum < minimum || current < minimum || current > maximum)
        {
            return Failed(displayDeviceName, description, physicalMonitorIndex, $"Hardware brightness returned an invalid range: {minimum}/{current}/{maximum}.");
        }

        return new HardwareBrightnessReading(
            displayDeviceName,
            description,
            physicalMonitorIndex,
            IsAvailable: true,
            (int)minimum,
            (int)current,
            (int)maximum,
            Error: null);
    }

    private static HardwareBrightnessReading Failed(string displayDeviceName, string physicalDescription, string error) =>
        Failed(displayDeviceName, physicalDescription, physicalMonitorIndex: 0, error);

    private static HardwareBrightnessReading Failed(string displayDeviceName, string physicalDescription, int physicalMonitorIndex, string error) =>
        new(
            displayDeviceName,
            physicalDescription,
            physicalMonitorIndex,
            IsAvailable: false,
            Minimum: 0,
            Current: 0,
            Maximum: 0,
            error);
}
