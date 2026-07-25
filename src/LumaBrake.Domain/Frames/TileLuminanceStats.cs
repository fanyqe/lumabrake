namespace LumaBrake.Domain.Frames;

public sealed record TileLuminanceStats(
    int Column,
    int Row,
    double AverageLuminance,
    double Percentile95Luminance,
    double BrightPixelRatio);
