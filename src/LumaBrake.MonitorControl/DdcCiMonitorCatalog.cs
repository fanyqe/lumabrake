using System.Runtime.InteropServices;
using System.Text;

namespace LumaBrake.MonitorControl;

public sealed class DdcCiMonitorCatalog
{
    private const int MaximumCapabilitiesLength = 65536;

    public IReadOnlyList<HardwareMonitorCapability> GetCapabilities() =>
        DdcCiPhysicalMonitorSession.Enumerate(Failed, ReadCapabilities);

    private static HardwareMonitorCapability ReadCapabilities(string displayDeviceName, PhysicalMonitor physicalMonitor)
    {
        var description = DdcCiPhysicalMonitorSession.DescriptionOrDeviceName(displayDeviceName, physicalMonitor);

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
}
