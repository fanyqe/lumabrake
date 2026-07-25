using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LumaBrake.Domain.Rules;

namespace LumaBrake.Platform.Windows;

public sealed class WindowsForegroundApplicationProvider : IForegroundApplicationProvider
{
    private const int WindowTitleCapacity = 512;
    private const int FullScreenPixelTolerance = 2;

    public ForegroundApplicationInfo Current()
    {
        var foregroundWindow = NativeMethods.GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return ForegroundApplicationInfo.Unknown;
        }

        try
        {
            var processName = ProcessName(foregroundWindow);
            var windowTitle = WindowTitle(foregroundWindow);
            var isFullScreen = IsFullScreenWindow(foregroundWindow);
            return new ForegroundApplicationInfo(processName, windowTitle, isFullScreen);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return ForegroundApplicationInfo.Unknown;
        }
    }

    private static string? ProcessName(IntPtr foregroundWindow)
    {
        _ = NativeMethods.GetWindowThreadProcessId(foregroundWindow, out var processId);
        if (processId == 0)
        {
            return null;
        }

        using var process = Process.GetProcessById((int)processId);
        return process.ProcessName;
    }

    private static string? WindowTitle(IntPtr foregroundWindow)
    {
        var title = new StringBuilder(WindowTitleCapacity);
        var length = NativeMethods.GetWindowText(foregroundWindow, title, title.Capacity);
        return length > 0 ? title.ToString() : null;
    }

    private static bool IsFullScreenWindow(IntPtr foregroundWindow)
    {
        var monitor = NativeMethods.MonitorFromWindow(foregroundWindow, NativeMethods.MonitorDefaultToNull);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new MonitorInfoEx
        {
            Size = Marshal.SizeOf<MonitorInfoEx>(),
            DeviceName = string.Empty
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info) ||
            !NativeMethods.GetWindowRect(foregroundWindow, out var windowRect))
        {
            return false;
        }

        return CoversMonitorBounds(windowRect, info.Monitor);
    }

    public static bool CoversMonitorBounds(NativeRect windowRect, NativeRect monitorRect)
    {
        var windowWidth = windowRect.Right - windowRect.Left;
        var windowHeight = windowRect.Bottom - windowRect.Top;
        if (windowWidth <= 0 || windowHeight <= 0)
        {
            return false;
        }

        return windowRect.Left <= monitorRect.Left + FullScreenPixelTolerance &&
            windowRect.Top <= monitorRect.Top + FullScreenPixelTolerance &&
            windowRect.Right >= monitorRect.Right - FullScreenPixelTolerance &&
            windowRect.Bottom >= monitorRect.Bottom - FullScreenPixelTolerance;
    }
}
