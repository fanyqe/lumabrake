using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Hardware;
using LumaBrake.Domain.Profiles;
using LumaBrake.Domain.Schedules;

namespace LumaBrake.Persistence;

public sealed record LumaBrakeSettings
{
    public bool Enabled { get; init; } = true;

    public double UserMaximumOpacity { get; init; } = 0.64;

    public double ManualDimmingOpacity { get; init; }

    public SpikeDetectorOptions SpikeGuard { get; init; } = new();

    public FlashDetectorOptions FlashGuard { get; init; } = new();

    public StaticShadeOptions StaticShade { get; init; } = new();

    public AdaptiveContentOptions AdaptiveContent { get; init; } = new();

    public InactiveMonitorOptions InactiveMonitor { get; init; } = new();

    public FocusDimmingOptions FocusDimming { get; init; } = new();

    public TintOptions Tint { get; init; } = new();

    public ScheduleOptions Schedules { get; init; } = new();

    public ProfileOptions Profiles { get; init; } = new();

    public StartupRestorationOptions StartupRestoration { get; init; } = new();

    public HardwareBrightnessOptions HardwareBrightness { get; init; } = new();
}
