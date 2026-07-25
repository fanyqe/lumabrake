namespace LumaBrake.Domain.Analysis;

public sealed record FlashDetectorOptions
{
    public bool Enabled { get; init; } = true;

    public TimeSpan Window { get; init; } = TimeSpan.FromMilliseconds(1200);

    public int MinimumTransitions { get; init; } = 3;

    public int EarlyLocalizedTransitions { get; init; } = 3;

    public TimeSpan MaximumTransitionInterval { get; init; } = TimeSpan.FromMilliseconds(550);

    public double BrightTileAverageThreshold { get; init; } = 0.62;

    public double BrightTilePixelRatioThreshold { get; init; } = 0.35;

    public double SignalTilePixelRatioThreshold { get; init; } = 0.08;

    public double DarkTileAverageThreshold { get; init; } = 0.18;

    public double MinimumTransitionDelta { get; init; } = 0.34;

    public double MinimumAffectedTileRatio { get; init; } = 0.0015;

    public double EarlyLocalizedMaximumAffectedTileRatio { get; init; } = 0.35;

    public LocalMaskFitPreset RegionalMaskFit { get; init; } = LocalMaskFitPreset.Precise;

    public double RegionalSignalPadding { get; init; } = 0.004;

    public bool SuppressLocalizedBrightSceneScroll { get; init; } = true;

    public bool RedFlashDetectionEnabled { get; init; } = true;

    public double RedDominanceThreshold { get; init; } = 0.35;

    public double RedDominantPixelRatioThreshold { get; init; } = 0.35;

    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMilliseconds(500);

    public double MaximumOpacity { get; init; } = 0.62;

    public TimeSpan Hold { get; init; } = TimeSpan.FromMilliseconds(320);

    public TimeSpan Release { get; init; } = TimeSpan.FromMilliseconds(950);
}
