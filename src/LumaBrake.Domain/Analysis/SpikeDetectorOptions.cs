namespace LumaBrake.Domain.Analysis;

public sealed record SpikeDetectorOptions
{
    public int WarmupSamples { get; init; } = 8;

    public int BaselineWindowSamples { get; init; } = 20;

    public double MinimumActivationLuminance { get; init; } = 0.28;

    public double AbsoluteIncreaseThreshold { get; init; } = 0.18;

    public double RelativeIncreaseThreshold { get; init; } = 1.85;

    public double BrightPixelRatioThreshold { get; init; } = 0.18;

    public double BrightTileAverageThreshold { get; init; } = 0.35;

    public double BrightTilePixelRatioThreshold { get; init; } = 0.20;

    public double TileAverageIncreaseThreshold { get; init; } = 0.14;

    public double MinimumBrightTileRatio { get; init; } = 0.10;

    public double MinimumChangedTileRatio { get; init; } = 0.18;

    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMilliseconds(900);

    public double MaximumOpacity { get; init; } = 0.58;

    public TimeSpan Hold { get; init; } = TimeSpan.FromMilliseconds(220);

    public TimeSpan Release { get; init; } = TimeSpan.FromMilliseconds(900);
}
