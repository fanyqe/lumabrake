using System.Runtime.InteropServices;
using System.Text;

namespace LumaBrake.MonitorControl;

internal static partial class MonitorControlNativeMethods
{
    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint numberOfPhysicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetPhysicalMonitorsFromHMONITOR(
        IntPtr hMonitor,
        uint physicalMonitorArraySize,
        [Out] PhysicalMonitor[] physicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyPhysicalMonitors(
        uint physicalMonitorArraySize,
        [In] PhysicalMonitor[] physicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCapabilitiesStringLength(IntPtr physicalMonitorHandle, out uint capabilitiesStringLengthInCharacters);

    [DllImport("dxva2.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CapabilitiesRequestAndCapabilitiesReply(
        IntPtr physicalMonitorHandle,
        StringBuilder capabilitiesString,
        uint capabilitiesStringLengthInCharacters);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorBrightness(
        IntPtr physicalMonitorHandle,
        out uint minimumBrightness,
        out uint currentBrightness,
        out uint maximumBrightness);
}
