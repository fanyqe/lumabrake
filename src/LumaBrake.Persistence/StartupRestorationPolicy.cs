using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Hardware;

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
                MonitorManualDimming = new MonitorManualDimmingOptions(),
                Tint = new TintOptions { Enabled = false },
                StartupRestoration = settings.StartupRestoration with { HardwareBrightness = new HardwareBrightnessTarget() }
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
            MonitorManualDimming = settings.MonitorManualDimming.Clamp(),
            Tint = schedule.Tint.ToEffect().Enabled ? ClampTint(schedule.Tint) : ClampTint(settings.Tint),
            StartupRestoration = settings.StartupRestoration with
            {
                HardwareBrightness = EffectiveHardwareBrightness(settings.StartupRestoration.HardwareBrightness, schedule.HardwareBrightness)
            }
        };
    }

    private static LumaBrakeSettings ClampSoftwareEffects(LumaBrakeSettings settings) =>
        settings with
        {
            ManualDimmingOpacity = Math.Clamp(settings.ManualDimmingOpacity, 0, 0.90),
            MonitorManualDimming = settings.MonitorManualDimming.Clamp(),
            Tint = ClampTint(settings.Tint),
            StartupRestoration = settings.StartupRestoration with
            {
                HardwareBrightness = settings.StartupRestoration.HardwareBrightness.Clamp()
            }
        };

    private static HardwareBrightnessTarget EffectiveHardwareBrightness(
        HardwareBrightnessTarget startupTarget,
        HardwareBrightnessTarget scheduleTarget)
    {
        var scheduled = scheduleTarget.Clamp();
        return scheduled.Enabled ? scheduled : startupTarget.Clamp();
    }

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
