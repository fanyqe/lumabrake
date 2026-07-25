using System.Runtime.InteropServices;
using LumaBrake.Domain.Monitors;

namespace LumaBrake.Platform.Windows;

public sealed class WindowsFocusedMonitorProvider : IFocusedMonitorProvider
{
    public string? FocusedMonitorStableId(IReadOnlyList<MonitorDescriptor> monitors)
    {
        if (monitors.Count == 0)
        {
            return null;
        }

        var foregroundWindow = NativeMethods.GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return null;
        }

        var monitorHandle = NativeMethods.MonitorFromWindow(foregroundWindow, NativeMethods.MonitorDefaultToNull);
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
