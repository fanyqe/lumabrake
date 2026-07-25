namespace LumaBrake.Domain.Frames;

public sealed record FrameAnalysisResult(
    FrameMetadata Metadata,
    double AverageLuminance,
    double PeakLuminance,
    double Percentile95Luminance,
    double BrightPixelRatio,
    FrameTileSummary? TileSummary = null);
