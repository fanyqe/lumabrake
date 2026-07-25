namespace LumaBrake.Domain.Monitors;

public interface IFocusedMonitorProvider
{
    string? FocusedMonitorStableId(IReadOnlyList<MonitorDescriptor> monitors);
}
