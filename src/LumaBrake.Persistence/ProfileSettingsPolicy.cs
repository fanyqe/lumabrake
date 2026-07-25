using LumaBrake.Domain.Profiles;

namespace LumaBrake.Persistence;

public static class ProfileSettingsPolicy
{
    public static LumaBrakeProfile Capture(string id, string? name, LumaBrakeSettings settings) =>
        new LumaBrakeProfile
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? id : name,
            UserMaximumOpacity = settings.UserMaximumOpacity,
            ManualDimmingOpacity = settings.ManualDimmingOpacity,
            MonitorManualDimming = settings.MonitorManualDimming,
            ProtectionTuning = settings.ProtectionTuning,
            Tint = settings.Tint,
            MonitorTints = settings.MonitorTints,
            NightColor = settings.NightColor,
            FocusDimming = settings.FocusDimming,
            HardwareBrightness = settings.StartupRestoration.HardwareBrightness
        }.Clamp();

    public static LumaBrakeSettings Apply(LumaBrakeSettings settings, LumaBrakeProfile profile)
    {
        var applied = profile.Clamp();
        var updated = settings with
        {
            UserMaximumOpacity = applied.UserMaximumOpacity,
            ManualDimmingOpacity = applied.ManualDimmingOpacity,
            MonitorManualDimming = applied.MonitorManualDimming,
            ProtectionTuning = applied.ProtectionTuning,
            Tint = applied.Tint,
            MonitorTints = applied.MonitorTints,
            NightColor = applied.NightColor,
            FocusDimming = applied.FocusDimming,
            StartupRestoration = settings.StartupRestoration with { HardwareBrightness = applied.HardwareBrightness }
        };

        return ProtectionTuningPolicy.Apply(updated) with
        {
            UserMaximumOpacity = applied.UserMaximumOpacity
        };
    }
}
