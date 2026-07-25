using LumaBrake.Analysis.Detection;
using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Tests;

public sealed class StaticShadeDetectorTests
{
    private readonly StaticShadeOptions _options = new()
    {
        MinimumStaticDuration = TimeSpan.FromMilliseconds(300),
        FullOpacityDuration = TimeSpan.FromMilliseconds(900),
        BrightTileAverageThreshold = 0.70,
        BrightTilePixelRatioThreshold = 0.50,
        MaximumAverageDelta = 0.02,
        MinimumAffectedTileRatio = 0.05,
        MaximumAffectedTileRatio = 0.50,
        BaseOpacity = 0.10,
        MaximumOpacity = 0.30
    };

    [Fact]
    public void Observe_DetectsLocalizedBrightStaticTileAfterDuration()
    {
        var detector = new StaticShadeDetector(_options);
        var events = LocalizedStaticSequence([0.80, 0.80, 0.80, 0.80])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var staticShade = Assert.Single(events);
        Assert.Equal(1, staticShade!.AffectedTileCount);
        Assert.Equal(0.0625, staticShade.AffectedTileRatio, precision: 12);
        Assert.Equal(TimeSpan.FromMilliseconds(300), staticShade.LongestStaticDuration);
        Assert.Equal(_options.BaseOpacity, staticShade.RecommendedOpacity, precision: 12);

        var region = Assert.Single(staticShade.AffectedRegions);
        Assert.Equal(0, region.X, precision: 12);
        Assert.Equal(0, region.Y, precision: 12);
        Assert.Equal(0.25, region.Width, precision: 12);
        Assert.Equal(0.25, region.Height, precision: 12);
    }

    [Fact]
    public void Observe_DoesNotTriggerBeforeMinimumDuration()
    {
        var detector = new StaticShadeDetector(_options);
        var events = LocalizedStaticSequence([0.80, 0.80, 0.80])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_ResetsWhenBrightTileChanges()
    {
        var detector = new StaticShadeDetector(_options);
        var events = LocalizedStaticSequence([0.80, 0.80, 0.40, 0.80, 0.80, 0.80])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_DoesNotCreateRegionalMaskForBroadStaticBrightFrame()
    {
        var detector = new StaticShadeDetector(_options);
        var events = UniformStaticSequence([0.80, 0.80, 0.80, 0.80])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_DoesNothingWhenDisabled()
    {
        var detector = new StaticShadeDetector(_options with { Enabled = false });
        var events = LocalizedStaticSequence([0.80, 0.80, 0.80, 0.80])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_ResetsStaticDurationAfterLongFrameGap()
    {
        var detector = new StaticShadeDetector(_options with { MaximumFrameGap = TimeSpan.FromMilliseconds(250) });
        var frames = LocalizedStaticSequence([0.80, 0.80, 0.80], stepMs: 100)
            .Concat(LocalizedStaticSequence([0.80, 0.80, 0.80], stepMs: 100, startMs: 1000));

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    private static IEnumerable<FrameAnalysisResult> LocalizedStaticSequence(
        IReadOnlyList<double> values,
        int stepMs = 100,
        int startMs = 0)
    {
        for (var index = 0; index < values.Count; index++)
        {
            yield return Frame(LocalizedTiles(values[index]), startMs + (index * stepMs));
        }
    }

    private static IEnumerable<FrameAnalysisResult> UniformStaticSequence(IReadOnlyList<double> values, int stepMs = 100)
    {
        for (var index = 0; index < values.Count; index++)
        {
            yield return Frame(UniformTiles(values[index]), index * stepMs);
        }
    }

    private static FrameAnalysisResult Frame(FrameTileSummary summary, int timestampMs) =>
        new(
            new FrameMetadata(DateTimeOffset.UnixEpoch.AddMilliseconds(timestampMs), 16, 9, "test"),
            AverageLuminance: summary.Tiles.Average(tile => tile.AverageLuminance),
            PeakLuminance: summary.Tiles.Max(tile => tile.AverageLuminance),
            Percentile95Luminance: summary.Tiles.Max(tile => tile.Percentile95Luminance),
            BrightPixelRatio: summary.Tiles.Average(tile => tile.BrightPixelRatio),
            TileSummary: summary);

    private static FrameTileSummary LocalizedTiles(double luminance)
    {
        var tiles = Enumerable.Range(0, 16)
            .Select(index => Tile(index, index == 0 ? luminance : 0.05))
            .ToArray();

        return new FrameTileSummary(4, 4, tiles);
    }

    private static FrameTileSummary UniformTiles(double luminance)
    {
        var tiles = Enumerable.Range(0, 16)
            .Select(index => Tile(index, luminance))
            .ToArray();

        return new FrameTileSummary(4, 4, tiles);
    }

    private static TileLuminanceStats Tile(int index, double luminance)
    {
        var brightRatio = luminance >= 0.70 ? 1 : 0;
        return new TileLuminanceStats(
            Column: index % 4,
            Row: index / 4,
            AverageLuminance: luminance,
            Percentile95Luminance: luminance,
            BrightPixelRatio: brightRatio);
    }
}
