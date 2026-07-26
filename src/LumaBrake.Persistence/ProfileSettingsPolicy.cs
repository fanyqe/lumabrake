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
            Tint = settings.Tint,
            MonitorTints = settings.MonitorTints,
            FocusDimming = settings.FocusDimming
        }.Clamp();

    public static LumaBrakeSettings Apply(LumaBrakeSettings settings, LumaBrakeProfile profile)
    {
        var applied = profile.Clamp();
        return settings with
        {
            UserMaximumOpacity = applied.UserMaximumOpacity,
            ManualDimmingOpacity = applied.ManualDimmingOpacity,
            Tint = applied.Tint,
            MonitorTints = applied.MonitorTints,
            FocusDimming = applied.FocusDimming
        };
    }
}
