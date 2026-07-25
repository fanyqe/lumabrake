using LumaBrake.Domain.Monitors;

namespace LumaBrake.Platform.Windows;

public static class PrimaryMonitor
{
    public static MonitorDescriptor Get()
    {
        var monitors = new WindowsMonitorEnumerator().GetMonitors();
        return monitors.FirstOrDefault(monitor => monitor.IsPrimary)
            ?? monitors.FirstOrDefault()
            ?? throw new InvalidOperationException("No active Windows monitor was found.");
    }

    public static MonitorBounds Bounds() => Get().Bounds;
}
