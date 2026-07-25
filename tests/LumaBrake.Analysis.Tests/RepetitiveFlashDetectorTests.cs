using LumaBrake.Analysis.Detection;
using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Tests;

public sealed class RepetitiveFlashDetectorTests
{
    private readonly FlashDetectorOptions _options = new()
    {
        Window = TimeSpan.FromMilliseconds(1000),
        MinimumTransitions = 3,
        MinimumAffectedTileRatio = 0.05,
        Cooldown = TimeSpan.FromMilliseconds(500)
    };

    [Fact]
    public void Observe_DetectsFullFrameRepetitiveFlash()
    {
        var detector = new RepetitiveFlashDetector(_options);
        var events = UniformSequence([0.05, 0.85, 0.05, 0.85])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var flash = Assert.Single(events);
        Assert.Equal(16, flash!.AffectedTileCount);
        Assert.Equal(1, flash.AffectedTileRatio, precision: 12);
        Assert.InRange(flash.RecommendedOpacity, 0.45, _options.MaximumOpacity);
    }

    [Fact]
    public void Observe_DoesNotTriggerOnIsolatedTransition()
    {
        var detector = new RepetitiveFlashDetector(_options);
        var events = UniformSequence([0.05, 0.85, 0.05])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_DetectsLocalizedTileFlash()
    {
        var detector = new RepetitiveFlashDetector(_options);
        var events = LocalizedSequence([0.05, 0.85, 0.05, 0.85])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var flash = Assert.Single(events);
        Assert.Equal(1, flash!.AffectedTileCount);
        Assert.Equal(0.0625, flash.AffectedTileRatio, precision: 12);
    }

    [Fact]
    public void Observe_PrunesTransitionsOutsideWindow()
    {
        var detector = new RepetitiveFlashDetector(_options with { Window = TimeSpan.FromMilliseconds(250) });
        var events = UniformSequence([0.05, 0.85, 0.05, 0.85], stepMs: 300)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    private static IEnumerable<FrameAnalysisResult> UniformSequence(IReadOnlyList<double> values, int stepMs = 100)
    {
        for (var index = 0; index < values.Count; index++)
        {
            yield return Frame(values[index], UniformTiles(values[index]), index * stepMs);
        }
    }

    private static IEnumerable<FrameAnalysisResult> LocalizedSequence(IReadOnlyList<double> values, int stepMs = 100)
    {
        for (var index = 0; index < values.Count; index++)
        {
            yield return Frame(values[index] / 16.0, LocalizedTiles(values[index]), index * stepMs);
        }
    }

    private static FrameAnalysisResult Frame(double average, FrameTileSummary summary, int timestampMs) =>
        new(
            new FrameMetadata(DateTimeOffset.UnixEpoch.AddMilliseconds(timestampMs), 16, 9, "test"),
            AverageLuminance: average,
            PeakLuminance: summary.Tiles.Max(tile => tile.AverageLuminance),
            Percentile95Luminance: summary.Tiles.Max(tile => tile.Percentile95Luminance),
            BrightPixelRatio: summary.Tiles.Average(tile => tile.BrightPixelRatio),
            TileSummary: summary);

    private static FrameTileSummary UniformTiles(double luminance)
    {
        var tiles = Enumerable.Range(0, 16)
            .Select(index => Tile(index, luminance))
            .ToArray();

        return new FrameTileSummary(4, 4, tiles);
    }

    private static FrameTileSummary LocalizedTiles(double luminance)
    {
        var tiles = Enumerable.Range(0, 16)
            .Select(index => Tile(index, index == 0 ? luminance : 0.05))
            .ToArray();

        return new FrameTileSummary(4, 4, tiles);
    }

    private static TileLuminanceStats Tile(int index, double luminance)
    {
        var brightRatio = luminance >= 0.62 ? 1 : 0;
        return new TileLuminanceStats(
            Column: index % 4,
            Row: index / 4,
            AverageLuminance: luminance,
            Percentile95Luminance: luminance,
            BrightPixelRatio: brightRatio);
    }
}
