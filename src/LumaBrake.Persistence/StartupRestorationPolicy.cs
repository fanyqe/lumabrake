using LumaBrake.Domain.Effects;

namespace LumaBrake.Persistence;

public static class StartupRestorationPolicy
{
    public static LumaBrakeSettings Apply(LumaBrakeSettings settings, DateTimeOffset localTimestamp)
    {
        if (!settings.StartupRestoration.Enabled)
        {
            return settings with
            {
                ManualDimmingOpacity = 0,
                Tint = new TintOptions { Enabled = false }
            };
        }

        if (!settings.StartupRestoration.ApplySchedules)
        {
            return ClampSoftwareEffects(settings);
        }

        var activeSchedule = settings.Schedules.ActiveAt(localTimestamp);
        if (activeSchedule is null)
        {
            return ClampSoftwareEffects(settings);
        }

        var schedule = activeSchedule.Clamp();
        return settings with
        {
            ManualDimmingOpacity = Math.Clamp(settings.ManualDimmingOpacity, 0, 0.90),
            Tint = schedule.Tint.ToEffect().Enabled ? ClampTint(schedule.Tint) : ClampTint(settings.Tint)
        };
    }

    private static LumaBrakeSettings ClampSoftwareEffects(LumaBrakeSettings settings) =>
        settings with
        {
            ManualDimmingOpacity = Math.Clamp(settings.ManualDimmingOpacity, 0, 0.90),
            Tint = ClampTint(settings.Tint)
        };

    private static TintOptions ClampTint(TintOptions tint)
    {
        var effect = tint.ToEffect();
        return effect.Enabled
            ? new TintOptions
            {
                Enabled = true,
                Red = effect.Red,
                Green = effect.Green,
                Blue = effect.Blue,
                Opacity = effect.Opacity
            }
            : new TintOptions { Enabled = false };
    }
}
