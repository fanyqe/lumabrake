namespace LumaBrake.Domain.Analysis;

public sealed record StaticShadeOptions
{
    public bool Enabled { get; init; } = true;

    public TimeSpan MinimumStaticDuration { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan FullOpacityDuration { get; init; } = TimeSpan.FromMinutes(3);

    public TimeSpan MaximumFrameGap { get; init; } = TimeSpan.FromSeconds(2);

    public double BrightTileAverageThreshold { get; init; } = 0.62;

    public double BrightTilePixelRatioThreshold { get; init; } = 0.35;

    public double MaximumAverageDelta { get; init; } = 0.025;

    public double MinimumAffectedTileRatio { get; init; } = 0.005;

    public double MaximumAffectedTileRatio { get; init; } = 0.45;

    public double BaseOpacity { get; init; } = 0.10;

    public double MaximumOpacity { get; init; } = 0.32;

    public TimeSpan Hold { get; init; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan Release { get; init; } = TimeSpan.FromMilliseconds(1200);
}
