namespace LumaBrake.Domain.Analysis;

public sealed record StaticShadeOptions
{
    public bool Enabled { get; init; } = true;

    public TimeSpan MinimumStaticDuration { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan FullOpacityDuration { get; init; } = TimeSpan.FromMinutes(3);

    public TimeSpan MaximumFrameGap { get; init; } = TimeSpan.FromSeconds(2);

    public double BrightTileAverageThreshold { get; init; } = 0.62;

    public double BrightTilePercentile95Threshold { get; init; } = 0.72;

    public double BrightTilePixelRatioThreshold { get; init; } = 0.35;

    public double MaximumAverageDelta { get; init; } = 0.025;

    public double MinimumAffectedTileRatio { get; init; } = 0.005;

    public double MaximumAffectedTileRatio { get; init; } = 0.45;

    public double MinimumExposureScore { get; init; } = 0.18;

    public double ExposureLuminanceWeight { get; init; } = 0.45;

    public double ExposureBrightPixelWeight { get; init; } = 0.25;

    public double ExposureDurationWeight { get; init; } = 0.30;

    public double BaseOpacity { get; init; } = 0.10;

    public double MaximumOpacity { get; init; } = 0.32;

    public TimeSpan Hold { get; init; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan Release { get; init; } = TimeSpan.FromMilliseconds(1200);

    public static StaticShadeOptions ForPreset(StaticShadePreset preset) =>
        preset switch
        {
            StaticShadePreset.Disabled => new StaticShadeOptions
            {
                Enabled = false
            },
            StaticShadePreset.OledConservative => new StaticShadeOptions
            {
                MinimumStaticDuration = TimeSpan.FromSeconds(20),
                FullOpacityDuration = TimeSpan.FromMinutes(2),
                BrightTileAverageThreshold = 0.58,
                BrightTilePercentile95Threshold = 0.66,
                BrightTilePixelRatioThreshold = 0.28,
                MinimumExposureScore = 0.14,
                MaximumAffectedTileRatio = 0.55,
                BaseOpacity = 0.12,
                MaximumOpacity = 0.36,
                Release = TimeSpan.FromMilliseconds(1400)
            },
            StaticShadePreset.OledAggressive => new StaticShadeOptions
            {
                MinimumStaticDuration = TimeSpan.FromSeconds(12),
                FullOpacityDuration = TimeSpan.FromSeconds(90),
                BrightTileAverageThreshold = 0.52,
                BrightTilePercentile95Threshold = 0.60,
                BrightTilePixelRatioThreshold = 0.22,
                MinimumExposureScore = 0.10,
                MaximumAffectedTileRatio = 0.60,
                BaseOpacity = 0.14,
                MaximumOpacity = 0.44,
                Hold = TimeSpan.FromMilliseconds(600),
                Release = TimeSpan.FromMilliseconds(1600)
            },
            StaticShadePreset.StaticHud => new StaticShadeOptions
            {
                MinimumStaticDuration = TimeSpan.FromSeconds(10),
                FullOpacityDuration = TimeSpan.FromSeconds(75),
                MinimumAffectedTileRatio = 0.002,
                MaximumAffectedTileRatio = 0.30,
                MinimumExposureScore = 0.12,
                BaseOpacity = 0.13,
                MaximumOpacity = 0.40
            },
            StaticShadePreset.OfficeUse => new StaticShadeOptions
            {
                MinimumStaticDuration = TimeSpan.FromMinutes(1),
                FullOpacityDuration = TimeSpan.FromMinutes(5),
                BrightTileAverageThreshold = 0.68,
                BrightTilePercentile95Threshold = 0.78,
                BrightTilePixelRatioThreshold = 0.45,
                MinimumExposureScore = 0.24,
                BaseOpacity = 0.08,
                MaximumOpacity = 0.24
            },
            _ => new StaticShadeOptions()
        };
}
