using System.Runtime.InteropServices;

namespace LumaBrake.MonitorControl;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct PhysicalMonitor
{
    public IntPtr Handle;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Description;
}
