using LumaBrake.Domain.Effects;

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
    long FramesAnalyzed,
    long SpikeEvents);
