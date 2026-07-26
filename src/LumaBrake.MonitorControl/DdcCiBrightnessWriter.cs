using System.Runtime.InteropServices;
using LumaBrake.Domain.Hardware;

namespace LumaBrake.MonitorControl;

public sealed class DdcCiBrightnessWriter
{
    public IReadOnlyList<HardwareBrightnessWriteResult> SetBrightness(
        string displayDeviceName,
        int physicalMonitorIndex,
        int requestedBrightness,
        HardwareBrightnessOptions options,
        DateTimeOffset now,
        DateTimeOffset? lastWriteAt)
    {
        var targetDisplayDeviceName = displayDeviceName.Trim();
        if (string.IsNullOrWhiteSpace(targetDisplayDeviceName))
        {
            return [Failed("unknown", "unknown", requestedBrightness, "Display device name is required.")];
        }

        if (physicalMonitorIndex < 0)
        {
            return [Failed(targetDisplayDeviceName, targetDisplayDeviceName, requestedBrightness, "Physical monitor index must be 0 or greater.")];
        }

        if (requestedBrightness is < 0 or > 100)
        {
            return [Failed(targetDisplayDeviceName, targetDisplayDeviceName, requestedBrightness, "Hardware brightness must be from 0 to 100.")];
        }

        var policy = new HardwareBrightnessPolicy(options);
        var blockReason = policy.WriteBlockReason(targetDisplayDeviceName, now, lastWriteAt);
        if (blockReason is not null)
        {
            return [Failed(targetDisplayDeviceName, targetDisplayDeviceName, requestedBrightness, blockReason)];
        }

        var results = DdcCiPhysicalMonitorSession.Enumerate(
            (deviceName, physicalDescription, error) => Failed(deviceName, physicalDescription, physicalMonitorIndex, requestedBrightness, error),
            (deviceName, physicalMonitor, index) => Write(deviceName, physicalMonitor, index, requestedBrightness),
            policy.IsBlacklisted,
            deviceName => !string.Equals(deviceName, targetDisplayDeviceName, StringComparison.OrdinalIgnoreCase),
            (deviceName, index) => !string.Equals(deviceName, targetDisplayDeviceName, StringComparison.OrdinalIgnoreCase) ||
                index != physicalMonitorIndex);

        return results.Count == 0
            ? [Failed(targetDisplayDeviceName, targetDisplayDeviceName, physicalMonitorIndex, requestedBrightness, "Display device or physical monitor index was not found.")]
            : results;
    }

    internal static uint ScaleBrightnessPercentage(int requestedBrightness, uint minimum, uint maximum)
    {
        if (maximum <= minimum)
        {
            return minimum;
        }

        var clamped = Math.Clamp(requestedBrightness, 0, 100);
        var scaled = minimum + ((maximum - minimum) * (clamped / 100.0));
        return (uint)Math.Round(scaled, MidpointRounding.AwayFromZero);
    }

    private static HardwareBrightnessWriteResult Write(
        string displayDeviceName,
        PhysicalMonitor physicalMonitor,
        int physicalMonitorIndex,
        int requestedBrightness)
    {
        var description = DdcCiPhysicalMonitorSession.DescriptionOrDeviceName(displayDeviceName, physicalMonitor);
        if (!MonitorControlNativeMethods.GetMonitorBrightness(
            physicalMonitor.Handle,
            out var minimum,
            out _,
            out var maximum))
        {
            return Failed(displayDeviceName, description, physicalMonitorIndex, requestedBrightness, $"Hardware brightness range could not be read before write. Win32 error: {Marshal.GetLastWin32Error()}.");
        }

        if (maximum < minimum)
        {
            return Failed(displayDeviceName, description, physicalMonitorIndex, requestedBrightness, $"Hardware brightness returned an invalid range: {minimum}/{maximum}.");
        }

        var appliedBrightness = ScaleBrightnessPercentage(requestedBrightness, minimum, maximum);
        if (!MonitorControlNativeMethods.SetMonitorBrightness(physicalMonitor.Handle, appliedBrightness))
        {
            return Failed(displayDeviceName, description, physicalMonitorIndex, requestedBrightness, $"Hardware brightness could not be written. Win32 error: {Marshal.GetLastWin32Error()}.");
        }

        return new HardwareBrightnessWriteResult(
            displayDeviceName,
            description,
            physicalMonitorIndex,
            requestedBrightness,
            (int)appliedBrightness,
            Succeeded: true,
            Error: null);
    }

    private static HardwareBrightnessWriteResult Failed(
        string displayDeviceName,
        string physicalDescription,
        int requestedBrightness,
        string error) =>
        Failed(displayDeviceName, physicalDescription, physicalMonitorIndex: 0, requestedBrightness, error);

    private static HardwareBrightnessWriteResult Failed(
        string displayDeviceName,
        string physicalDescription,
        int physicalMonitorIndex,
        int requestedBrightness,
        string error) =>
        new(
            displayDeviceName,
            physicalDescription,
            physicalMonitorIndex,
            requestedBrightness,
            AppliedHardwareBrightness: 0,
            Succeeded: false,
            error);
}
