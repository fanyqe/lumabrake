namespace LumaBrake.Domain.Monitors;

public interface IMonitorCatalog
{
    IReadOnlyList<MonitorDescriptor> GetMonitors();
}
