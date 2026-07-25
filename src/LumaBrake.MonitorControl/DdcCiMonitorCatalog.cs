using System.Runtime.InteropServices;
using System.Text;
using LumaBrake.Platform.Windows;

namespace LumaBrake.MonitorControl;

public sealed class DdcCiMonitorCatalog
{
    private const int MaximumCapabilitiesLength = 65536;

    public IReadOnlyList<HardwareMonitorCapability> GetCapabilities()
    {
        var capabilities = new List<HardwareMonitorCapability>();
        var success = NativeMethods.EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (IntPtr monitorHandle, IntPtr hdcMonitor, ref NativeRect monitorRect, IntPtr state) =>
            {
                var deviceName = DisplayDeviceName(monitorHandle);
                AddPhysicalMonitorCapabilities(monitorHandle, deviceName, capabilities);
                return true;
            },
            IntPtr.Zero);

        if (!success)
        {
            throw new InvalidOperationException($"Windows display monitors could not be enumerated. Win32 error: {Marshal.GetLastWin32Error()}.");
        }

        return capabilities;
    }

    private static void AddPhysicalMonitorCapabilities(
        IntPtr monitorHandle,
        string displayDeviceName,
        List<HardwareMonitorCapability> capabilities)
    {
        try
        {
            if (!MonitorControlNativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(monitorHandle, out var count) || count == 0)
            {
                capabilities.Add(Failed(displayDeviceName, displayDeviceName, "Physical monitor handles are unavailable."));
                return;
            }

            var physicalMonitors = new PhysicalMonitor[count];
            if (!MonitorControlNativeMethods.GetPhysicalMonitorsFromHMONITOR(monitorHandle, count, physicalMonitors))
            {
                capabilities.Add(Failed(displayDeviceName, displayDeviceName, $"Physical monitor handles could not be opened. Win32 error: {Marshal.GetLastWin32Error()}."));
                return;
            }

            try
            {
                foreach (var physicalMonitor in physicalMonitors)
                {
                    capabilities.Add(ReadCapabilities(displayDeviceName, physicalMonitor));
                }
            }
            finally
            {
                _ = MonitorControlNativeMethods.DestroyPhysicalMonitors(count, physicalMonitors);
            }
        }
        catch (DllNotFoundException ex)
        {
            capabilities.Add(Failed(displayDeviceName, displayDeviceName, ex.Message));
        }
        catch (EntryPointNotFoundException ex)
        {
            capabilities.Add(Failed(displayDeviceName, displayDeviceName, ex.Message));
        }
    }

    private static HardwareMonitorCapability ReadCapabilities(string displayDeviceName, PhysicalMonitor physicalMonitor)
    {
        var description = string.IsNullOrWhiteSpace(physicalMonitor.Description)
            ? displayDeviceName
            : physicalMonitor.Description.TrimEnd('\0');

        if (!MonitorControlNativeMethods.GetCapabilitiesStringLength(physicalMonitor.Handle, out var length) || length == 0)
        {
            return Failed(displayDeviceName, description, $"DDC/CI capabilities are unavailable. Win32 error: {Marshal.GetLastWin32Error()}.");
        }

        if (length > MaximumCapabilitiesLength)
        {
            return Failed(displayDeviceName, description, $"DDC/CI capabilities length is unexpectedly large: {length}.");
        }

        var builder = new StringBuilder((int)length);
        if (!MonitorControlNativeMethods.CapabilitiesRequestAndCapabilitiesReply(physicalMonitor.Handle, builder, length))
        {
            return Failed(displayDeviceName, description, $"DDC/CI capabilities could not be read. Win32 error: {Marshal.GetLastWin32Error()}.");
        }

        var capabilityText = builder.ToString().TrimEnd('\0');
        return new HardwareMonitorCapability(
            displayDeviceName,
            description,
            SupportsDdcCi: true,
            capabilityText.Length,
            Error: null);
    }

    private static HardwareMonitorCapability Failed(string displayDeviceName, string physicalDescription, string error) =>
        new(displayDeviceName, physicalDescription, SupportsDdcCi: false, CapabilitiesLength: 0, error);

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
