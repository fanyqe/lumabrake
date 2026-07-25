using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Color;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Hardware;
using LumaBrake.Domain.Profiles;
using LumaBrake.Domain.Rules;
using LumaBrake.Domain.Schedules;

namespace LumaBrake.Persistence;

public sealed record LumaBrakeSettings
{
    public int SchemaVersion { get; init; } = SettingsMigration.CurrentSchemaVersion;

    public bool Enabled { get; init; } = true;

    public double UserMaximumOpacity { get; init; } = 0.64;

    public double ManualDimmingOpacity { get; init; }

    public MonitorManualDimmingOptions MonitorManualDimming { get; init; } = new();

    public ProtectionTuningOptions ProtectionTuning { get; init; } = new();

    public SpikeDetectorOptions SpikeGuard { get; init; } = new();

    public FlashDetectorOptions FlashGuard { get; init; } = new();

    public StaticShadeOptions StaticShade { get; init; } = new();

    public AdaptiveContentOptions AdaptiveContent { get; init; } = new();

    public InactiveMonitorOptions InactiveMonitor { get; init; } = new();

    public FocusDimmingOptions FocusDimming { get; init; } = new();

    public TintOptions Tint { get; init; } = new();

    public NightColorOptions NightColor { get; init; } = new();

    public MonitorTintOptions MonitorTints { get; init; } = new();

    public ScheduleOptions Schedules { get; init; } = new();

    public ProfileOptions Profiles { get; init; } = new();

    public ApplicationRuleOptions ApplicationRules { get; init; } = new();

    public StartupRestorationOptions StartupRestoration { get; init; } = new();

    public HardwareBrightnessOptions HardwareBrightness { get; init; } = new();
}
