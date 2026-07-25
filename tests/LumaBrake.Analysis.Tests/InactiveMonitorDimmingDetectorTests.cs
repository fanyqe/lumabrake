using LumaBrake.Analysis.Detection;
using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Tests;

public sealed class InactiveMonitorDimmingDetectorTests
{
    private readonly InactiveMonitorOptions _options = new()
    {
        MinimumInactiveDuration = TimeSpan.FromMilliseconds(200),
        MaximumFrameGap = TimeSpan.FromMilliseconds(500),
        MaximumAverageDelta = 0.01,
        MaximumPercentile95Delta = 0.02,
        MaximumChangedTileRatio = 0.08,
        TileChangeAverageDelta = 0.08,
        TileChangePercentile95Delta = 0.10,
        MinimumAverageLuminance = 0.20,
        Opacity = 0.22
    };

    [Fact]
    public void Observe_DetectsUnchangedBrightFrameAfterDuration()
    {
        var detector = new InactiveMonitorDimmingDetector(_options);
        var events = Sequence([Frame(0.60, 0.82), Frame(0.60, 0.82), Frame(0.60, 0.82), Frame(0.60, 0.82)])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var inactive = Assert.Single(events);
        Assert.Equal(TimeSpan.FromMilliseconds(200), inactive!.InactiveDuration);
        Assert.Equal(_options.Opacity, inactive.RecommendedOpacity, precision: 12);
    }

    [Fact]
    public void Observe_ResetsWhenFrameChanges()
    {
        var detector = new InactiveMonitorDimmingDetector(_options);
        var events = Sequence([Frame(0.60, 0.82), Frame(0.60, 0.82), Frame(0.72, 0.90), Frame(0.72, 0.90)])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_ResetsWhenTilesMoveButGlobalMetricsStayStable()
    {
        var detector = new InactiveMonitorDimmingDetector(_options);
        var events = Sequence(
            [
                Frame(TileLayout([0, 1])),
                Frame(TileLayout([0, 1])),
                Frame(TileLayout([2, 3])),
                Frame(TileLayout([2, 3])),
                Frame(TileLayout([2, 3]))
            ])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_IgnoresSmallTileNoiseBelowActivityRatio()
    {
        var detector = new InactiveMonitorDimmingDetector(_options);
        var events = Sequence(
            [
                Frame(TileLayout([0, 1])),
                Frame(TileLayout([0, 1])),
                Frame(TileLayout([0, 1], noisyDarkTileIndex: 2)),
                Frame(TileLayout([0, 1], noisyDarkTileIndex: 2)),
                Frame(TileLayout([0, 1], noisyDarkTileIndex: 2))
            ])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Equal(
            [TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(300)],
            events.Select(inactive => inactive!.InactiveDuration));
    }

    [Fact]
    public void Observe_DoesNotDimDarkUnchangedFrame()
    {
        var detector = new InactiveMonitorDimmingDetector(_options);
        var events = Sequence([Frame(0.08, 0.10), Frame(0.08, 0.10), Frame(0.08, 0.10), Frame(0.08, 0.10)])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_DoesNothingWhenDisabled()
    {
        var detector = new InactiveMonitorDimmingDetector(_options with { Enabled = false });
        var events = Sequence([Frame(0.60, 0.82), Frame(0.60, 0.82), Frame(0.60, 0.82), Frame(0.60, 0.82)])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_ResetsAfterLongFrameGap()
    {
        var detector = new InactiveMonitorDimmingDetector(_options);
        var frames = Sequence([Frame(0.60, 0.82), Frame(0.60, 0.82), Frame(0.60, 0.82)], stepMs: 100)
            .Concat(Sequence([Frame(0.60, 0.82), Frame(0.60, 0.82), Frame(0.60, 0.82)], stepMs: 100, startMs: 1000));

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    private static IEnumerable<FrameAnalysisResult> Sequence(
        IReadOnlyList<FrameAnalysisResult> frames,
        int stepMs = 100,
        int startMs = 0)
    {
        for (var index = 0; index < frames.Count; index++)
        {
            yield return frames[index] with
            {
                Metadata = new FrameMetadata(
                    DateTimeOffset.UnixEpoch.AddMilliseconds(startMs + (index * stepMs)),
                    16,
                    9,
                    "test")
            };
        }
    }

    private static FrameAnalysisResult Frame(double average, double percentile95) =>
        new(
            new FrameMetadata(DateTimeOffset.UnixEpoch, 16, 9, "test"),
            AverageLuminance: average,
            PeakLuminance: percentile95,
            Percentile95Luminance: percentile95,
            BrightPixelRatio: average >= 0.20 ? 0.50 : 0,
            TileSummary: null);

    private static FrameAnalysisResult Frame(FrameTileSummary tileSummary)
    {
        var average = tileSummary.Tiles.Average(tile => tile.AverageLuminance);
        var percentile95 = tileSummary.Tiles.Max(tile => tile.Percentile95Luminance);
        return new FrameAnalysisResult(
            new FrameMetadata(DateTimeOffset.UnixEpoch, 16, 9, "test"),
            AverageLuminance: average,
            PeakLuminance: percentile95,
            Percentile95Luminance: percentile95,
            BrightPixelRatio: tileSummary.Tiles.Average(tile => tile.BrightPixelRatio),
            TileSummary: tileSummary);
    }

    private static FrameTileSummary TileLayout(
        IReadOnlyCollection<int> brightTileIndexes,
        int? noisyDarkTileIndex = null)
    {
        var tiles = Enumerable.Range(0, 16)
            .Select(index => new TileLuminanceStats(
                Column: index % 4,
                Row: index / 4,
                AverageLuminance: TileAverage(index, brightTileIndexes, noisyDarkTileIndex),
                Percentile95Luminance: TilePercentile95(index, brightTileIndexes, noisyDarkTileIndex),
                BrightPixelRatio: brightTileIndexes.Contains(index) ? 1.0 : 0.10))
            .ToArray();

        return new FrameTileSummary(4, 4, tiles);
    }

    private static double TileAverage(
        int index,
        IReadOnlyCollection<int> brightTileIndexes,
        int? noisyDarkTileIndex) =>
        brightTileIndexes.Contains(index) ? 0.90 : index == noisyDarkTileIndex ? 0.42 : 0.30;

    private static double TilePercentile95(
        int index,
        IReadOnlyCollection<int> brightTileIndexes,
        int? noisyDarkTileIndex) =>
        brightTileIndexes.Contains(index) ? 0.95 : index == noisyDarkTileIndex ? 0.47 : 0.35;
}
