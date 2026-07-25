namespace LumaBrake.Domain.Analysis;

public sealed record FlashDetectorOptions
{
    public TimeSpan Window { get; init; } = TimeSpan.FromMilliseconds(1200);

    public int MinimumTransitions { get; init; } = 4;

    public double BrightTileAverageThreshold { get; init; } = 0.62;

    public double BrightTilePixelRatioThreshold { get; init; } = 0.35;

    public double DarkTileAverageThreshold { get; init; } = 0.18;

    public double MinimumTransitionDelta { get; init; } = 0.34;

    public double MinimumAffectedTileRatio { get; init; } = 0.03;

    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMilliseconds(650);

    public double MaximumOpacity { get; init; } = 0.62;

    public TimeSpan Hold { get; init; } = TimeSpan.FromMilliseconds(320);

    public TimeSpan Release { get; init; } = TimeSpan.FromMilliseconds(950);
}
