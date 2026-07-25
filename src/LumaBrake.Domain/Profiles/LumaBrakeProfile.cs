using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Color;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Hardware;

namespace LumaBrake.Domain.Profiles;

public sealed record LumaBrakeProfile
{
    public string Id { get; init; } = "profile";

    public string Name { get; init; } = "Profile";

    public double UserMaximumOpacity { get; init; } = 0.64;

    public double ManualDimmingOpacity { get; init; }

    public MonitorManualDimmingOptions MonitorManualDimming { get; init; } = new();

    public ProtectionTuningOptions ProtectionTuning { get; init; } = new();

    public TintOptions Tint { get; init; } = new();

    public MonitorTintOptions MonitorTints { get; init; } = new();

    public NightColorOptions NightColor { get; init; } = new();

    public FocusDimmingOptions FocusDimming { get; init; } = new();

    public HardwareBrightnessTarget HardwareBrightness { get; init; } = new();

    public LumaBrakeProfile Clamp()
    {
        var id = string.IsNullOrWhiteSpace(Id) ? "profile" : Id.Trim();
        var name = string.IsNullOrWhiteSpace(Name) ? id : Name.Trim();

        return this with
        {
            Id = id,
            Name = name,
            UserMaximumOpacity = Math.Clamp(UserMaximumOpacity, 0.05, 0.92),
            ManualDimmingOpacity = Math.Clamp(ManualDimmingOpacity, 0, 0.90),
            MonitorManualDimming = MonitorManualDimming.Clamp(),
            ProtectionTuning = ProtectionTuning.Clamp(),
            Tint = Tint.ToEffect().Enabled ? ClampTint(Tint) : new TintOptions { Enabled = false },
            MonitorTints = MonitorTints.Clamp(),
            NightColor = NightColor.Clamp(),
            FocusDimming = FocusDimming.Clamp(),
            HardwareBrightness = HardwareBrightness.Clamp()
        };
    }

    private static TintOptions ClampTint(TintOptions tint)
    {
        var effect = tint.ToEffect();
        return new TintOptions
        {
            Enabled = effect.Enabled,
            Red = effect.Red,
            Green = effect.Green,
            Blue = effect.Blue,
            Opacity = effect.Opacity
        };
    }
}
