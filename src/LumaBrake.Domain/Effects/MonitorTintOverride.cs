using LumaBrake.Domain.Monitors;

namespace LumaBrake.Domain.Effects;

public sealed record MonitorTintOverride
{
    public string MonitorId { get; init; } = string.Empty;

    public TintOptions Tint { get; init; } = new();

    public MonitorTintOverride Clamp()
    {
        var effect = Tint.ToEffect();
        return this with
        {
            MonitorId = MonitorId.Trim(),
            Tint = effect.Enabled
                ? new TintOptions
                {
                    Enabled = true,
                    Red = effect.Red,
                    Green = effect.Green,
                    Blue = effect.Blue,
                    Opacity = effect.Opacity
                }
                : new TintOptions { Enabled = false }
        };
    }

    public bool Matches(MonitorDescriptor monitor)
    {
        var id = MonitorId.Trim();
        return !string.IsNullOrWhiteSpace(id) &&
            (string.Equals(id, monitor.StableId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
    }
}
