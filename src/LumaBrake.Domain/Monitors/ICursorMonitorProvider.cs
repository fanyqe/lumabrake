namespace LumaBrake.Domain.Monitors;

public interface ICursorMonitorProvider
{
    string? CursorMonitorStableId(IReadOnlyList<MonitorDescriptor> monitors);
}
