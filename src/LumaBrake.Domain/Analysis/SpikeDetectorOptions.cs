namespace LumaBrake.Domain.Analysis;

public sealed record SpikeDetectorOptions
{
    public bool Enabled { get; init; } = true;

    public int WarmupSamples { get; init; } = 3;

    public int BaselineWindowSamples { get; init; } = 20;

    public double MinimumActivationLuminance { get; init; } = 0.28;

    public double AbsoluteIncreaseThreshold { get; init; } = 0.18;

    public double RelativeIncreaseThreshold { get; init; } = 1.85;

    public double BrightPixelRatioThreshold { get; init; } = 0.18;

    public double BrightTileAverageThreshold { get; init; } = 0.35;

    public double BrightTilePixelRatioThreshold { get; init; } = 0.20;

    public double TileAverageIncreaseThreshold { get; init; } = 0.14;

    public double TileBrightPixelRatioIncreaseThreshold { get; init; } = 0.08;

    public double MinimumBrightTileRatio { get; init; } = 0.10;

    public double MinimumChangedTileRatio { get; init; } = 0.18;

    public bool LocalizedSpikeDetectionEnabled { get; init; }

    public double MinimumLocalizedChangedTileRatio { get; init; } = 0.0015;

    public double MaximumLocalizedChangedTileRatio { get; init; } = 0.35;

    public LocalMaskFitPreset LocalizedMaskFit { get; init; } = LocalMaskFitPreset.Precise;

    public double LocalizedSignalPadding { get; init; } = 0.004;

    public bool SuppressLocalizedBrightSceneScroll { get; init; } = true;

    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMilliseconds(900);

    public double MaximumOpacity { get; init; } = 0.58;

    public TimeSpan Hold { get; init; } = TimeSpan.FromMilliseconds(220);

    public TimeSpan Release { get; init; } = TimeSpan.FromMilliseconds(900);
}
