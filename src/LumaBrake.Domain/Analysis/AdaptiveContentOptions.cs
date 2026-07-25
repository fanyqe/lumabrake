namespace LumaBrake.Domain.Analysis;

public sealed record AdaptiveContentOptions
{
    public bool Enabled { get; init; } = true;

    public TimeSpan MinimumBrightDuration { get; init; } = TimeSpan.FromMilliseconds(900);

    public TimeSpan MaximumFrameGap { get; init; } = TimeSpan.FromSeconds(2);

    public double MinimumAverageLuminance { get; init; } = 0.42;

    public double MinimumPercentile95Luminance { get; init; } = 0.70;

    public double MinimumBrightPixelRatio { get; init; } = 0.35;

    public double SmoothingFactor { get; init; } = 0.20;

    public double DeadZone { get; init; } = 0.015;

    public double MinimumOpacity { get; init; } = 0.05;

    public double MaximumOpacity { get; init; } = 0.24;

    public TimeSpan Hold { get; init; } = TimeSpan.FromMilliseconds(300);

    public TimeSpan Release { get; init; } = TimeSpan.FromMilliseconds(900);
}
