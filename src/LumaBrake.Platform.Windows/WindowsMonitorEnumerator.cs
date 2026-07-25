using System.Runtime.InteropServices;
using LumaBrake.Domain.Monitors;

namespace LumaBrake.Platform.Windows;

public sealed class WindowsMonitorEnumerator : IMonitorCatalog
{
    public IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
        var monitors = new List<MonitorDescriptor>();
        var seenAt = DateTimeOffset.UtcNow;
        var advancedColorByDevice = WindowsAdvancedColorReader.ReadBySourceDeviceName();

        var success = NativeMethods.EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (IntPtr monitorHandle, IntPtr hdcMonitor, ref NativeRect monitorRect, IntPtr state) =>
            {
                var info = new MonitorInfoEx
                {
                    Size = Marshal.SizeOf<MonitorInfoEx>(),
                    DeviceName = string.Empty
                };

                if (!NativeMethods.GetMonitorInfo(monitorHandle, ref info))
                {
                    return true;
                }

                var deviceName = info.DeviceName.TrimEnd('\0');
                var displayName = ResolveDisplayName(deviceName);
                advancedColorByDevice.TryGetValue(deviceName, out var advancedColor);
                monitors.Add(new MonitorDescriptor(
                    StableId(deviceName, displayName, info.Monitor),
                    deviceName,
                    displayName,
                    (info.Flags & NativeMethods.MonitorinfofPrimary) != 0,
                    IsActive: true,
                    ToBounds(info.Monitor),
                    ToBounds(info.WorkArea),
                    DpiScale: ResolveDpiScale(monitorHandle),
                    LastSeenAt: seenAt)
                {
                    AdvancedColor = advancedColor ?? MonitorAdvancedColorState.Unknown
                });

                return true;
            },
            IntPtr.Zero);

        if (!success)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Windows display monitors could not be enumerated. Win32 error: {error}.");
        }

        return monitors
            .OrderByDescending(monitor => monitor.IsPrimary)
            .ThenBy(monitor => monitor.Bounds.Left)
            .ThenBy(monitor => monitor.Bounds.Top)
            .ToArray();
    }

    private static MonitorBounds ToBounds(NativeRect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private static string ResolveDisplayName(string deviceName)
    {
        var displayDevice = new DisplayDevice
        {
            Size = Marshal.SizeOf<DisplayDevice>(),
            DeviceName = string.Empty,
            DeviceString = string.Empty,
            DeviceId = string.Empty,
            DeviceKey = string.Empty
        };

        if (!NativeMethods.EnumDisplayDevices(deviceName, 0, ref displayDevice, 0))
        {
            return deviceName;
        }

        var displayName = displayDevice.DeviceString.TrimEnd('\0');
        return string.IsNullOrWhiteSpace(displayName) ? deviceName : displayName;
    }

    private static string StableId(string deviceName, string displayName, NativeRect bounds) =>
        $"{deviceName}|{displayName}|{bounds.Left},{bounds.Top},{bounds.Right - bounds.Left}x{bounds.Bottom - bounds.Top}";

    private static double ResolveDpiScale(IntPtr monitorHandle)
    {
        try
        {
            var result = NativeMethods.GetDpiForMonitor(
                monitorHandle,
                NativeMethods.MdtEffectiveDpi,
                out var dpiX,
                out _);

            return result == 0 && dpiX > 0 ? dpiX / 96.0 : 1.0;
        }
        catch (DllNotFoundException)
        {
            return 1.0;
        }
        catch (EntryPointNotFoundException)
        {
            return 1.0;
        }
    }
}
