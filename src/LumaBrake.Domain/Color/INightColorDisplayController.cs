using LumaBrake.Domain.Monitors;

namespace LumaBrake.Domain.Color;

public interface INightColorDisplayController : IDisposable
{
    bool Apply(IReadOnlyList<MonitorDescriptor> monitors, NightColorState state);

    void Clear();
}
