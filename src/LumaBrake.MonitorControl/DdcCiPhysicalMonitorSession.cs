using System.Runtime.InteropServices;
using LumaBrake.Platform.Windows;

namespace LumaBrake.MonitorControl;

internal static class DdcCiPhysicalMonitorSession
{
    public static IReadOnlyList<T> Enumerate<T>(
        Func<string, string, string, T> failed,
        Func<string, PhysicalMonitor, T> read,
        Func<string, bool>? shouldSkipDisplayDeviceName = null)
    {
        var results = new List<T>();
        var success = NativeMethods.EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (IntPtr monitorHandle, IntPtr hdcMonitor, ref NativeRect monitorRect, IntPtr state) =>
            {
                var deviceName = DisplayDeviceName(monitorHandle);
                if (shouldSkipDisplayDeviceName?.Invoke(deviceName) == true)
                {
                    results.Add(failed(deviceName, deviceName, "Hardware brightness is blacklisted for this display device."));
                    return true;
                }

                AddPhysicalMonitorResults(monitorHandle, deviceName, results, failed, read);
                return true;
            },
            IntPtr.Zero);

        if (!success)
        {
            throw new InvalidOperationException($"Windows display monitors could not be enumerated. Win32 error: {Marshal.GetLastWin32Error()}.");
        }

        return results;
    }

    private static void AddPhysicalMonitorResults<T>(
        IntPtr monitorHandle,
        string displayDeviceName,
        List<T> results,
        Func<string, string, string, T> failed,
        Func<string, PhysicalMonitor, T> read)
    {
        try
        {
            if (!MonitorControlNativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(monitorHandle, out var count) || count == 0)
            {
                results.Add(failed(displayDeviceName, displayDeviceName, "Physical monitor handles are unavailable."));
                return;
            }

            var physicalMonitors = new PhysicalMonitor[count];
            if (!MonitorControlNativeMethods.GetPhysicalMonitorsFromHMONITOR(monitorHandle, count, physicalMonitors))
            {
                results.Add(failed(displayDeviceName, displayDeviceName, $"Physical monitor handles could not be opened. Win32 error: {Marshal.GetLastWin32Error()}."));
                return;
            }

            try
            {
                foreach (var physicalMonitor in physicalMonitors)
                {
                    results.Add(read(displayDeviceName, physicalMonitor));
                }
            }
            finally
            {
                _ = MonitorControlNativeMethods.DestroyPhysicalMonitors(count, physicalMonitors);
            }
        }
        catch (DllNotFoundException ex)
        {
            results.Add(failed(displayDeviceName, displayDeviceName, ex.Message));
        }
        catch (EntryPointNotFoundException ex)
        {
            results.Add(failed(displayDeviceName, displayDeviceName, ex.Message));
        }
    }

    public static string DescriptionOrDeviceName(string displayDeviceName, PhysicalMonitor physicalMonitor) =>
        string.IsNullOrWhiteSpace(physicalMonitor.Description)
            ? displayDeviceName
            : physicalMonitor.Description.TrimEnd('\0');

    private static string DisplayDeviceName(IntPtr monitorHandle)
    {
        var info = new MonitorInfoEx
        {
            Size = Marshal.SizeOf<MonitorInfoEx>(),
            DeviceName = string.Empty
        };

        return NativeMethods.GetMonitorInfo(monitorHandle, ref info)
            ? info.DeviceName.TrimEnd('\0')
            : "unknown";
    }
}
