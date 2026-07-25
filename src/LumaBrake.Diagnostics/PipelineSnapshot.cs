using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Monitors;

namespace LumaBrake.Diagnostics;

public sealed record PipelineSnapshot(
    DateTimeOffset Timestamp,
    bool IsEnabled,
    double AverageLuminance,
    double Percentile95Luminance,
    double OverlayOpacity,
    EffectReason ActiveReason,
    string ActiveMonitor,
    int MonitorCount,
    string MonitorStatus,
    long TransientFailureCount,
    DateTimeOffset? LastTransientFailureAt,
    long CaptureAnomalyEvents,
    DateTimeOffset? LastCaptureAnomalyAt,
    bool HasActiveCaptureAnomaly,
    long CaptureFailureEvents,
    DateTimeOffset? LastCaptureFailureAt,
    bool HasActiveCaptureFailure,
    long FramesAnalyzed,
    long SpikeEvents,
    long FlashEvents,
    long RedFlashEvents,
    int RegionalMaskCount,
    double RegionalMaskMaxOpacity,
    IReadOnlyList<MonitorPipelineSnapshot> Monitors)
{
    public IReadOnlyList<RegionalEffectMask> RegionalMasks { get; init; } = Array.Empty<RegionalEffectMask>();
}

public sealed record MonitorPipelineSnapshot(
    string StableId,
    string DeviceName,
    string DisplayName,
    bool IsPrimary,
    double AverageLuminance,
    double Percentile95Luminance,
    double OverlayOpacity,
    EffectReason ActiveReason,
    long FramesAnalyzed,
    long CaptureAnomalyEvents,
    DateTimeOffset? LastCaptureAnomalyAt,
    bool IsCaptureAnomalyActive,
    long CaptureFailureEvents,
    DateTimeOffset? LastCaptureFailureAt,
    bool IsCaptureFailureActive,
    long SpikeEvents,
    long FlashEvents,
    long RedFlashEvents,
    int RegionalMaskCount,
    double RegionalMaskMaxOpacity,
    MonitorAdvancedColorState AdvancedColor)
{
    public IReadOnlyList<RegionalEffectMask> RegionalMasks { get; init; } = Array.Empty<RegionalEffectMask>();
}
