using LumaBrake.Analysis.Detection;
using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Tests;

public sealed class BrightnessSpikeDetectorTests
{
    private readonly SpikeDetectorOptions _options = new()
    {
        WarmupSamples = 4,
        BaselineWindowSamples = 8,
        MinimumActivationLuminance = 0.25,
        AbsoluteIncreaseThreshold = 0.16,
        RelativeIncreaseThreshold = 1.8,
        BrightPixelRatioThreshold = 0.10,
        LocalizedSpikeDetectionEnabled = true,
        Cooldown = TimeSpan.FromMilliseconds(500)
    };

    [Fact]
    public void Observe_DetectsAbruptDarkToBrightTransition()
    {
        var detector = new BrightnessSpikeDetector(_options);
        SpikeEvent? detected = null;

        foreach (var frame in Sequence([0.08, 0.09, 0.08, 0.09, 0.86]))
        {
            detected = detector.Observe(frame);
        }

        Assert.NotNull(detected);
        Assert.InRange(detected.Delta, 0.70, 0.80);
        Assert.InRange(detected.RecommendedOpacity, 0.22, _options.MaximumOpacity);
    }

    [Fact]
    public void Observe_DefaultOptionsDetectAbruptSpikeOnFourthFrame()
    {
        var detector = new BrightnessSpikeDetector(new SpikeDetectorOptions());
        var events = Sequence([0.08, 0.09, 0.08, 0.86], stepMs: 50)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var spike = Assert.Single(events);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(150), spike!.Timestamp);
        Assert.InRange(spike.RecommendedOpacity, 0.22, new SpikeDetectorOptions().MaximumOpacity);
    }

    [Fact]
    public void Observe_DefaultOptionsDoNotCreateRegionalSpikeForLocalizedScrollLikeChange()
    {
        var detector = new BrightnessSpikeDetector(new SpikeDetectorOptions());
        var frames = Sequence([0.08, 0.08, 0.08]).Concat(
        [
            LocalizedBrightFrame(DateTimeOffset.UnixEpoch.AddMilliseconds(300))
        ]);

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_DoesNotTreatBrightPageScrollAsSpike()
    {
        var detector = new BrightnessSpikeDetector(_options);
        var frames = new[]
        {
            BroadBrightPageFrame(DateTimeOffset.UnixEpoch, tileLuminance: 0.38, average: 0.32, percentile95: 0.82, brightPixelRatio: 0.45),
            BroadBrightPageFrame(DateTimeOffset.UnixEpoch.AddMilliseconds(100), tileLuminance: 0.38, average: 0.32, percentile95: 0.82, brightPixelRatio: 0.45),
            BroadBrightPageFrame(DateTimeOffset.UnixEpoch.AddMilliseconds(200), tileLuminance: 0.38, average: 0.32, percentile95: 0.82, brightPixelRatio: 0.45),
            BroadBrightPageFrame(DateTimeOffset.UnixEpoch.AddMilliseconds(300), tileLuminance: 0.38, average: 0.32, percentile95: 0.82, brightPixelRatio: 0.45),
            BroadBrightPageFrame(DateTimeOffset.UnixEpoch.AddMilliseconds(400), tileLuminance: 0.72, average: 0.36, percentile95: 0.86, brightPixelRatio: 0.60)
        };

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_DoesNotTriggerDuringWarmup()
    {
        var detector = new BrightnessSpikeDetector(_options);
        var first = Sequence([0.9]).Single();

        Assert.Null(detector.Observe(first));
    }

    [Fact]
    public void Observe_DoesNotTriggerWhenDisabled()
    {
        var detector = new BrightnessSpikeDetector(_options with { Enabled = false });
        var events = Sequence([0.08, 0.09, 0.08, 0.09, 0.86])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_DoesNotTriggerOnGradualRamp()
    {
        var detector = new BrightnessSpikeDetector(_options);
        var events = Sequence([0.10, 0.12, 0.14, 0.16, 0.20, 0.24, 0.30, 0.36, 0.42])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_RespectsCooldown()
    {
        var detector = new BrightnessSpikeDetector(_options);
        var events = Sequence([0.08, 0.09, 0.08, 0.09, 0.86, 0.08, 0.86], 100)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Single(events);
    }

    [Fact]
    public void Observe_DetectsSuddenLocalizedBrightRegionAsRegionalSpike()
    {
        var detector = new BrightnessSpikeDetector(_options);
        var frames = Sequence([0.08, 0.09, 0.08, 0.09]).Concat(
        [
            LocalizedBrightFrame(DateTimeOffset.UnixEpoch.AddMilliseconds(500))
        ]);

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var spike = Assert.Single(events);
        Assert.True(spike!.IsRegional);
        var region = Assert.Single(spike.AffectedRegions);
        Assert.True(region.X <= 0);
        Assert.True(region.Right >= 0.50);
        Assert.True(region.Bottom >= 0.25);
        Assert.InRange(spike.RecommendedOpacity, 0.22, _options.MaximumOpacity);
    }

    [Fact]
    public void Observe_DetectsLocalizedSpikeFromBrightPixelRatioIncrease()
    {
        var detector = new BrightnessSpikeDetector(_options);
        var frames = Sequence([0.08, 0.08, 0.08, 0.08]).Concat(
        [
            LocalizedSmallBrightFrame(DateTimeOffset.UnixEpoch.AddMilliseconds(500))
        ]);

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var spike = Assert.Single(events);
        Assert.True(spike!.IsRegional);
        var region = Assert.Single(spike.AffectedRegions);
        Assert.InRange(spike.Delta, 0.07, 0.09);
        Assert.InRange(region.X, 0.08, 0.15);
        Assert.InRange(region.Width, 0.07, 0.14);
    }

    [Fact]
    public void Observe_DoesNotTriggerTinyWideGridLocalizedSpikeWithoutConfirmation()
    {
        var detector = new BrightnessSpikeDetector(_options);
        var frames = WideGridSequence([0.08, 0.08, 0.08, 0.08]).Concat(
        [
            WideGridSmallBrightFrame(DateTimeOffset.UnixEpoch.AddMilliseconds(500))
        ]);

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_ConfirmsTinyWideGridLocalizedSpikeWhenRegionPersists()
    {
        var detector = new BrightnessSpikeDetector(_options);
        var frames = WideGridSequence([0.08, 0.08, 0.08, 0.08]).Concat(
        [
            WideGridSmallBrightFrame(DateTimeOffset.UnixEpoch.AddMilliseconds(500)),
            WideGridSmallBrightFrame(DateTimeOffset.UnixEpoch.AddMilliseconds(600))
        ]);

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var spike = Assert.Single(events);
        Assert.True(spike!.IsRegional);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(600), spike.Timestamp);
        Assert.Single(spike.AffectedRegions);
    }

    [Fact]
    public void Observe_DoesNotTreatTileCorrelatedVerticalScrollAsLocalizedSpike()
    {
        var detector = new BrightnessSpikeDetector(_options with
        {
            Cooldown = TimeSpan.Zero,
            MinimumBrightTileRatio = 0.08,
            MinimumChangedTileRatio = 0.08
        });

        var events = TexturedVerticalScrollSequence(frameCount: 8)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_DoesNotUseStaleBaselineAfterLongFrameGap()
    {
        var detector = new BrightnessSpikeDetector(_options);
        var frames = Sequence([0.08, 0.09, 0.08, 0.09])
            .Concat(Sequence([0.86, 0.86, 0.86, 0.86], stepMs: 100, startMs: 900));

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_DoesNotTriggerAgainForUnchangedBrightSceneAfterCooldown()
    {
        var detector = new BrightnessSpikeDetector(_options);
        var frames = Sequence([0.08, 0.09, 0.08, 0.09])
            .Concat(Sequence([0.86], stepMs: 100, startMs: 500))
            .Concat(Sequence([0.86], stepMs: 100, startMs: 1200));

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Single(events);
    }

    private static IEnumerable<FrameAnalysisResult> Sequence(IReadOnlyList<double> luminance, int stepMs = 100, int startMs = 0)
    {
        for (var i = 0; i < luminance.Count; i++)
        {
            var value = luminance[i];
            yield return new FrameAnalysisResult(
                new FrameMetadata(DateTimeOffset.UnixEpoch.AddMilliseconds(startMs + (i * stepMs)), 1, 1, "test"),
                value,
                value,
                value,
                value >= 0.7 ? 1 : 0,
                UniformTiles(value));
        }
    }

    private static IEnumerable<FrameAnalysisResult> WideGridSequence(
        IReadOnlyList<double> luminance,
        int stepMs = 100,
        int startMs = 0)
    {
        for (var i = 0; i < luminance.Count; i++)
        {
            var value = luminance[i];
            yield return new FrameAnalysisResult(
                new FrameMetadata(DateTimeOffset.UnixEpoch.AddMilliseconds(startMs + (i * stepMs)), 16, 9, "test"),
                value,
                value,
                value,
                value >= 0.7 ? 1 : 0,
                WideGridUniformTiles(value));
        }
    }

    private static FrameAnalysisResult LocalizedBrightFrame(DateTimeOffset timestamp)
    {
        var tiles = Enumerable.Range(0, 16)
            .Select(index =>
            {
                var value = index < 2 ? 1 : 0;
                return new TileLuminanceStats(
                    Column: index % 4,
                    Row: index / 4,
                    AverageLuminance: value,
                    Percentile95Luminance: value,
                    BrightPixelRatio: value);
            })
            .ToArray();

        return new FrameAnalysisResult(
            new FrameMetadata(timestamp, 16, 9, "test"),
            AverageLuminance: 0.125,
            PeakLuminance: 1,
            Percentile95Luminance: 1,
            BrightPixelRatio: 0.125,
            TileSummary: new FrameTileSummary(4, 4, tiles));
    }

    private static FrameAnalysisResult LocalizedSmallBrightFrame(DateTimeOffset timestamp)
    {
        var signalBounds = new EffectRegion(0.10, 0.15, 0.08, 0.10);
        var tiles = Enumerable.Range(0, 16)
            .Select(index =>
            {
                var isTarget = index == 0;
                return new TileLuminanceStats(
                    Column: index % 4,
                    Row: index / 4,
                    AverageLuminance: isTarget ? 0.16 : 0.08,
                    Percentile95Luminance: isTarget ? 1 : 0.08,
                    BrightPixelRatio: isTarget ? 0.10 : 0,
                    SignalBounds: isTarget ? signalBounds : null);
            })
            .ToArray();

        return new FrameAnalysisResult(
            new FrameMetadata(timestamp, 16, 9, "test"),
            AverageLuminance: 0.085,
            PeakLuminance: 1,
            Percentile95Luminance: 1,
            BrightPixelRatio: 0.01,
            TileSummary: new FrameTileSummary(4, 4, tiles));
    }

    private static FrameAnalysisResult WideGridSmallBrightFrame(DateTimeOffset timestamp)
    {
        var signalBounds = new EffectRegion(0.10, 0.15, 0.08, 0.10);
        var tiles = Enumerable.Range(0, 576)
            .Select(index =>
            {
                var isTarget = index == 0;
                return new TileLuminanceStats(
                    Column: index % 32,
                    Row: index / 32,
                    AverageLuminance: isTarget ? 0.16 : 0.08,
                    Percentile95Luminance: isTarget ? 1 : 0.08,
                    BrightPixelRatio: isTarget ? 0.10 : 0,
                    SignalBounds: isTarget ? signalBounds : null);
            })
            .ToArray();

        return new FrameAnalysisResult(
            new FrameMetadata(timestamp, 16, 9, "test"),
            AverageLuminance: tiles.Average(tile => tile.AverageLuminance),
            PeakLuminance: 1,
            Percentile95Luminance: 1,
            BrightPixelRatio: tiles.Average(tile => tile.BrightPixelRatio),
            TileSummary: new FrameTileSummary(32, 18, tiles));
    }

    private static IEnumerable<FrameAnalysisResult> TexturedVerticalScrollSequence(int frameCount, int stepMs = 80)
    {
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var summary = TexturedVerticalScrollTiles(frameIndex);
            yield return new FrameAnalysisResult(
                new FrameMetadata(DateTimeOffset.UnixEpoch.AddMilliseconds(frameIndex * stepMs), 16, 9, "test"),
                AverageLuminance: summary.Tiles.Average(tile => tile.AverageLuminance),
                PeakLuminance: summary.Tiles.Max(tile => tile.Percentile95Luminance),
                Percentile95Luminance: summary.Tiles.Max(tile => tile.Percentile95Luminance),
                BrightPixelRatio: summary.Tiles.Average(tile => tile.BrightPixelRatio),
                TileSummary: summary);
        }
    }

    private static FrameTileSummary TexturedVerticalScrollTiles(int scrollOffsetRows)
    {
        const int columns = 32;
        const int rows = 18;
        var tiles = Enumerable.Range(0, columns * rows)
            .Select(index =>
            {
                var column = index % columns;
                var row = index / columns;
                var contentRow = row + scrollOffsetRows;
                var isBrightCardColumn = column is >= 4 and <= 7;
                var isBrightCard = isBrightCardColumn && contentRow % 2 == 0;
                var texture = 0.055 + (((contentRow * 13 + column * 7) % 9) * 0.008);
                var luminance = isBrightCard ? 0.84 : texture;
                return new TileLuminanceStats(
                    column,
                    row,
                    luminance,
                    luminance,
                    isBrightCard ? 1 : 0);
            })
            .ToArray();

        return new FrameTileSummary(columns, rows, tiles);
    }

    private static FrameAnalysisResult BroadBrightPageFrame(
        DateTimeOffset timestamp,
        double tileLuminance,
        double average,
        double percentile95,
        double brightPixelRatio)
    {
        var tiles = Enumerable.Range(0, 16)
            .Select(index => new TileLuminanceStats(
                Column: index % 4,
                Row: index / 4,
                AverageLuminance: tileLuminance,
                Percentile95Luminance: percentile95,
                BrightPixelRatio: brightPixelRatio))
            .ToArray();

        return new FrameAnalysisResult(
            new FrameMetadata(timestamp, 16, 9, "test"),
            AverageLuminance: average,
            PeakLuminance: percentile95,
            Percentile95Luminance: percentile95,
            BrightPixelRatio: brightPixelRatio,
            TileSummary: new FrameTileSummary(4, 4, tiles));
    }

    private static FrameTileSummary UniformTiles(double luminance)
    {
        var brightRatio = luminance >= 0.7 ? 1 : 0;
        var tiles = Enumerable.Range(0, 16)
            .Select(index => new TileLuminanceStats(
                Column: index % 4,
                Row: index / 4,
                AverageLuminance: luminance,
                Percentile95Luminance: luminance,
                BrightPixelRatio: brightRatio))
            .ToArray();

        return new FrameTileSummary(4, 4, tiles);
    }

    private static FrameTileSummary WideGridUniformTiles(double luminance)
    {
        var brightRatio = luminance >= 0.7 ? 1 : 0;
        var tiles = Enumerable.Range(0, 576)
            .Select(index => new TileLuminanceStats(
                Column: index % 32,
                Row: index / 32,
                AverageLuminance: luminance,
                Percentile95Luminance: luminance,
                BrightPixelRatio: brightRatio))
            .ToArray();

        return new FrameTileSummary(32, 18, tiles);
    }
}
