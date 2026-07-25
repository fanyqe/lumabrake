using System.Runtime.InteropServices;
using LumaBrake.Domain.Monitors;

namespace LumaBrake.Platform.Windows;

public sealed class WindowsCursorMonitorProvider : ICursorMonitorProvider
{
    public string? CursorMonitorStableId(IReadOnlyList<MonitorDescriptor> monitors)
    {
        if (monitors.Count == 0 || !NativeMethods.GetCursorPos(out var point))
        {
            return null;
        }

        var monitorHandle = NativeMethods.MonitorFromPoint(point, NativeMethods.MonitorDefaultToNull);
        if (monitorHandle == IntPtr.Zero)
        {
            return null;
        }

        var info = new MonitorInfoEx
        {
            Size = Marshal.SizeOf<MonitorInfoEx>(),
            DeviceName = string.Empty
        };

        if (!NativeMethods.GetMonitorInfo(monitorHandle, ref info))
        {
            return null;
        }

        var deviceName = info.DeviceName.TrimEnd('\0');
        return monitors
            .FirstOrDefault(monitor => string.Equals(monitor.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            ?.StableId;
    }
}
