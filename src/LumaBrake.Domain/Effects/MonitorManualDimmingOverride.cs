using LumaBrake.Domain.Monitors;

namespace LumaBrake.Domain.Effects;

public sealed record MonitorManualDimmingOverride
{
    public string MonitorId { get; init; } = string.Empty;

    public double Opacity { get; init; }

    public MonitorManualDimmingOverride Clamp() => this with
    {
        MonitorId = MonitorId.Trim(),
        Opacity = Math.Clamp(Opacity, 0, 0.90)
    };

    public bool Matches(MonitorDescriptor monitor)
    {
        var id = MonitorId.Trim();
        return !string.IsNullOrWhiteSpace(id) &&
            (string.Equals(id, monitor.StableId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
    }
}
