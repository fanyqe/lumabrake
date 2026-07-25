using LumaBrake.Analysis.Detection;
using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Tests;

public sealed class StaticShadeDetectorTests
{
    private readonly StaticShadeOptions _options = new()
    {
        MinimumStaticDuration = TimeSpan.FromMilliseconds(300),
        FullOpacityDuration = TimeSpan.FromMilliseconds(900),
        BrightTileAverageThreshold = 0.70,
        BrightTilePercentile95Threshold = 0.76,
        BrightTilePixelRatioThreshold = 0.50,
        MaximumAverageDelta = 0.02,
        MinimumAffectedTileRatio = 0.05,
        MaximumAffectedTileRatio = 0.50,
        MinimumExposureScore = 0.18,
        BaseOpacity = 0.10,
        MaximumOpacity = 0.30
    };

    [Fact]
    public void Observe_DetectsLocalizedBrightStaticTileAfterDuration()
    {
        var detector = new StaticShadeDetector(_options);
        var events = LocalizedStaticSequence([0.80, 0.80, 0.80, 0.80, 0.80])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var staticShade = Assert.Single(events);
        Assert.Equal(1, staticShade!.AffectedTileCount);
        Assert.Equal(0.0625, staticShade.AffectedTileRatio, precision: 12);
        Assert.Equal(TimeSpan.FromMilliseconds(400), staticShade.LongestStaticDuration);
        Assert.Equal(0.45, staticShade.ExposureScore, precision: 12);
        Assert.Equal(0.19, staticShade.RecommendedOpacity, precision: 12);

        var region = Assert.Single(staticShade.AffectedRegions);
        Assert.True(region.X <= 0);
        Assert.True(region.Y <= 0);
        Assert.True(region.Right >= 0.25);
        Assert.True(region.Bottom >= 0.25);
    }

    [Fact]
    public void Observe_DetectsStaticToolbarOverMovingNeutralContent()
    {
        var detector = new StaticShadeDetector(_options);
        var events = MovingNeutralContentWithStaticToolbar(frameCount: 5)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var staticShade = Assert.Single(events);
        Assert.Equal(1, staticShade!.AffectedTileCount);
        Assert.Equal(0.0625, staticShade.AffectedTileRatio, precision: 12);
        Assert.Equal(TimeSpan.FromMilliseconds(400), staticShade.LongestStaticDuration);
        Assert.True(staticShade.RecommendedOpacity >= _options.BaseOpacity);
        var region = Assert.Single(staticShade.AffectedRegions);
        Assert.True(region.X <= 0.25);
        Assert.True(region.Right >= 0.50);
        Assert.True(region.Y <= 0);
        Assert.True(region.Bottom >= 0.25);
    }

    [Fact]
    public void Observe_UsesSignalBoundsForStaticBrightTile()
    {
        var detector = new StaticShadeDetector(_options);
        var signal = new EffectRegion(0.08, 0.08, 0.05, 0.06);
        var events = LocalizedStaticSequence([0.80, 0.80, 0.80, 0.80, 0.80], signalBounds: signal)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var staticShade = Assert.Single(events);
        var region = Assert.Single(staticShade!.AffectedRegions);
        Assert.True(region.X <= signal.X);
        Assert.True(region.Y <= signal.Y);
        Assert.True(region.Right >= signal.Right - 0.0001);
        Assert.True(region.Bottom >= signal.Bottom - 0.0001);
        Assert.True(region.Width < 0.25);
        Assert.True(region.Height < 0.25);
    }

    [Fact]
    public void Observe_MergesAdjacentStaticBrightTilesIntoOneMask()
    {
        var detector = new StaticShadeDetector(_options);
        var events = AdjacentStaticSequence([0.80, 0.80, 0.80, 0.80, 0.80])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var staticShade = Assert.Single(events);
        Assert.Equal(4, staticShade!.AffectedTileCount);
        var region = Assert.Single(staticShade.AffectedRegions);
        Assert.True(region.X <= 0);
        Assert.True(region.Y <= 0);
        Assert.True(region.Right >= 0.50);
        Assert.True(region.Bottom >= 0.50);
    }

    [Fact]
    public void Observe_UsesSignalBoundsForConnectedStaticSurface()
    {
        var detector = new StaticShadeDetector(_options);
        var events = ConnectedSignalSurfaceSequence(frameCount: 5)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var staticShade = Assert.Single(events);
        Assert.Equal(2, staticShade!.AffectedTileCount);
        var region = Assert.Single(staticShade.AffectedRegions);
        Assert.InRange(region.Y, 0.08, 0.12);
        Assert.InRange(region.Bottom, 0.17, 0.21);
        Assert.True(region.Width < 0.50);
        Assert.True(region.Height < 0.16);
    }

    [Fact]
    public void Observe_KeepsTexturedTextboxStaticWhenAverageFlickersSlightly()
    {
        var detector = new StaticShadeDetector(_options);
        var signal = new EffectRegion(0.04, 0.06, 0.18, 0.14);
        var events = LocalizedStaticSequence(
                [0.80, 0.74, 0.79, 0.75, 0.80],
                percentile95: 0.92,
                brightPixelRatio: 0.44,
                signalBounds: signal)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var staticShade = Assert.Single(events);
        var region = Assert.Single(staticShade!.AffectedRegions);
        Assert.True(region.X <= signal.X);
        Assert.True(region.Y <= signal.Y);
        Assert.True(region.Right >= signal.Right);
        Assert.True(region.Bottom >= signal.Bottom);
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
    public void Observe_DoesNotCreateRegionalMaskForOneScrollLikeBrightReveal()
    {
        var detector = new StaticShadeDetector(_options);
        var firstPosition = new EffectRegion(0.05, 0.05, 0.10, 0.10);
        var secondPosition = new EffectRegion(0.55, 0.55, 0.10, 0.10);
        var frames = LocalizedStaticSequence([0.80, 0.80, 0.80, 0.80], signalBounds: firstPosition)
            .Concat(LocalizedStaticSequence([0.80, 0.80, 0.80, 0.80], startMs: 400, signalBounds: secondPosition, targetIndex: 10));

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_ReacquiresStaticShadeQuicklyForOverlappingRegionAfterRecentActivation()
    {
        var options = _options with
        {
            ReacquireWindow = TimeSpan.FromMilliseconds(1000),
            ReacquireMinimumStaticDuration = TimeSpan.FromMilliseconds(200)
        };
        var detector = new StaticShadeDetector(options);
        var firstPosition = new EffectRegion(0.05, 0.05, 0.10, 0.10);
        var secondPosition = new EffectRegion(0.08, 0.06, 0.10, 0.10);
        var frames = LocalizedStaticSequence([0.80, 0.80, 0.80, 0.80, 0.80], signalBounds: firstPosition)
            .Concat(LocalizedStaticSequence([0.80, 0.80, 0.80, 0.80], startMs: 500, signalBounds: secondPosition));

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.True(events.Length >= 2);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(400), events[0]!.Timestamp);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(700), events[1]!.Timestamp);
        Assert.Equal(TimeSpan.FromMilliseconds(300), events[1]!.Timestamp - events[0]!.Timestamp);
    }

    [Fact]
    public void Observe_DoesNotFastReacquireUnrelatedScrollRegionAfterRecentActivation()
    {
        var options = _options with
        {
            MinimumStaticDuration = TimeSpan.FromMilliseconds(1000),
            FullOpacityDuration = TimeSpan.FromMilliseconds(1800),
            ReacquireWindow = TimeSpan.FromMilliseconds(1000),
            ReacquireMinimumStaticDuration = TimeSpan.FromMilliseconds(200)
        };
        var detector = new StaticShadeDetector(options);
        var firstPosition = new EffectRegion(0.05, 0.05, 0.10, 0.10);
        var secondPosition = new EffectRegion(0.55, 0.55, 0.10, 0.10);
        var frames = LocalizedStaticSequence(Enumerable.Repeat(0.80, 12).ToArray(), signalBounds: firstPosition)
            .Concat(LocalizedStaticSequence(
                [0.80, 0.80, 0.80, 0.80],
                startMs: 1300,
                signalBounds: secondPosition,
                targetIndex: 10));

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.NotEmpty(events);
        Assert.DoesNotContain(events, result =>
            result!.Timestamp >= DateTimeOffset.UnixEpoch.AddMilliseconds(1300) &&
            result.AffectedRegions.Any(region => region.X > 0.45 && region.Y > 0.45));
    }

    [Fact]
    public void Observe_PreservesStaticHistoryAndMovesMaskWithVerticalScroll()
    {
        var detector = new StaticShadeDetector(_options);
        var frames = Enumerable.Range(0, 5)
            .Select(index => Frame(MotionPatternTiles(columnOffset: 0, rowOffset: 0), index * 100))
            .Concat([
                Frame(MotionPatternTiles(columnOffset: 0, rowOffset: 1), 500),
                Frame(MotionPatternTiles(columnOffset: 0, rowOffset: 1), 600)
            ]);

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.True(events.Length >= 2);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(400), events[0]!.Timestamp);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(500), events[1]!.Timestamp);
        Assert.True(events[1]!.LongestStaticDuration >= TimeSpan.FromMilliseconds(500));
        Assert.True(Assert.Single(events[1]!.AffectedRegions).Y > 0.25);
    }

    [Fact]
    public void Observe_PreservesStaticHistoryAndMovesMaskWithHorizontalScroll()
    {
        var detector = new StaticShadeDetector(_options);
        var frames = Enumerable.Range(0, 5)
            .Select(index => Frame(MotionPatternTiles(columnOffset: 0, rowOffset: 0), index * 100))
            .Concat([
                Frame(MotionPatternTiles(columnOffset: 1, rowOffset: 0), 500),
                Frame(MotionPatternTiles(columnOffset: 1, rowOffset: 0), 600)
            ]);

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.True(events.Length >= 2);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(500), events[1]!.Timestamp);
        Assert.True(events[1]!.LongestStaticDuration >= TimeSpan.FromMilliseconds(500));
        Assert.True(Assert.Single(events[1]!.AffectedRegions).X > 0.50);
    }

    [Fact]
    public void Observe_PreservesStaticHistoryForLikelyRepeatedListScroll()
    {
        var options = _options with
        {
            MinimumAffectedTileRatio = 0.01,
            MaximumAffectedTileRatio = 0.20
        };
        var detector = new StaticShadeDetector(options);
        var previous = RepeatedBrightListTiles(scrollOffsetRows: 0);
        var current = RepeatedBrightListTiles(scrollOffsetRows: 1);
        var motion = FrameTileMotionEstimator.Estimate(previous, current);
        var frames = Enumerable.Range(0, 5)
            .Select(index => Frame(RepeatedBrightListTiles(scrollOffsetRows: 0), index * 100))
            .Concat([
                Frame(RepeatedBrightListTiles(scrollOffsetRows: 1), 500),
                Frame(RepeatedBrightListTiles(scrollOffsetRows: 1), 600)
            ]);

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.True(motion.HasMotion);
        Assert.True(motion.IsMotionLikely);
        Assert.False(motion.IsSceneChange);
        Assert.True(events.Length >= 2);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(400), events[0]!.Timestamp);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(500), events[1]!.Timestamp);
        Assert.True(events[1]!.LongestStaticDuration >= TimeSpan.FromMilliseconds(500));
        Assert.Contains(events[1]!.AffectedRegions, region => region.Y > 0.05);
    }

    [Fact]
    public void Observe_DoesNotCarryStaticHistoryToScrolledReplacementWithDifferentContext()
    {
        var options = _options with
        {
            MinimumAffectedTileRatio = 0.005,
            MaximumAffectedTileRatio = 0.20
        };
        var detector = new StaticShadeDetector(options);
        var previous = LargeTrackedScrollTiles(rowOffset: 0);
        var replacement = LargeTrackedScrollTiles(
            rowOffset: 1,
            includeTrackedBrightContent: false,
            replacementAtShiftedTarget: true,
            disturbReplacementNeighborhood: true);
        var motion = FrameTileMotionEstimator.Estimate(previous, replacement);
        var frames = Enumerable.Range(0, 5)
            .Select(index => Frame(LargeTrackedScrollTiles(rowOffset: 0), index * 100))
            .Concat([
                Frame(replacement, 500),
                Frame(replacement, 600)
            ]);

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.True(motion.HasMotion);
        Assert.True(motion.IsReliable || motion.IsMotionLikely);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(400), events[0]!.Timestamp);
        Assert.Contains(events, result =>
            result!.Timestamp == DateTimeOffset.UnixEpoch.AddMilliseconds(500) &&
            result.RecommendedOpacity == 0 &&
            result.AffectedRegions.Count == 0);
        Assert.DoesNotContain(events, result =>
            result!.Timestamp >= DateTimeOffset.UnixEpoch.AddMilliseconds(500) &&
            result.RecommendedOpacity > 0 &&
            result.LongestStaticDuration >= TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void Observe_PreservesFixedToolbarHistoryDuringPageScroll()
    {
        var options = _options with
        {
            MinimumAffectedTileRatio = 0.02,
            MaximumAffectedTileRatio = 0.20
        };
        var detector = new StaticShadeDetector(options);
        var previous = PageScrollWithFixedToolbarTiles(rowOffset: 0);
        var current = PageScrollWithFixedToolbarTiles(rowOffset: 1);
        var motion = FrameTileMotionEstimator.Estimate(previous, current);
        var frames = Enumerable.Range(0, 5)
            .Select(index => Frame(PageScrollWithFixedToolbarTiles(rowOffset: 0), index * 100))
            .Concat([
                Frame(current, 500),
                Frame(PageScrollWithFixedToolbarTiles(rowOffset: 2), 600)
            ]);

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.True(motion.HasMotion);
        Assert.True(motion.IsReliable || motion.IsMotionLikely);
        Assert.True(events.Length >= 3);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(400), events[0]!.Timestamp);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(500), events[1]!.Timestamp);
        Assert.True(events[1]!.LongestStaticDuration >= TimeSpan.FromMilliseconds(500));
        Assert.True(events[1]!.AffectedRegions.All(region => region.Y <= 0.02));
    }

    [Fact]
    public void Observe_ClearsTrackedMaskAfterSceneChange()
    {
        var detector = new StaticShadeDetector(_options);
        var frames = Enumerable.Range(0, 5)
            .Select(index => Frame(MotionPatternTiles(columnOffset: 0, rowOffset: 0), index * 100))
            .Append(Frame(UniformTiles(0.05), 500));

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(400), events[0]!.Timestamp);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(500), events[1]!.Timestamp);
        Assert.Equal(0, events[1]!.RecommendedOpacity, precision: 12);
        Assert.Empty(events[1]!.AffectedRegions);
    }

    [Fact]
    public void Observe_EmitsSingleClearWhenTrackedStaticRegionDisappears()
    {
        var detector = new StaticShadeDetector(_options);
        var frames = LocalizedStaticSequence([0.80, 0.80, 0.80, 0.80, 0.80])
            .Concat(LocalizedStaticSequence([0.05, 0.05, 0.05], startMs: 500));

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Equal(2, events.Length);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(400), events[0]!.Timestamp);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(500), events[1]!.Timestamp);
        Assert.True(events[0]!.RecommendedOpacity > 0);
        Assert.Equal(0, events[1]!.RecommendedOpacity, precision: 12);
        Assert.Empty(events[1]!.AffectedRegions);
    }

    [Fact]
    public void Observe_ClearsTrackedMaskAfterViewportResize()
    {
        var detector = new StaticShadeDetector(_options);
        var frames = Enumerable.Range(0, 5)
            .Select(index => Frame(MotionPatternTiles(columnOffset: 0, rowOffset: 0), index * 100))
            .Append(Frame(MotionPatternTiles(columnOffset: 0, rowOffset: 0), 500, width: 24, height: 12));

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(400), events[0]!.Timestamp);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(500), events[1]!.Timestamp);
        Assert.Equal(0, events[1]!.RecommendedOpacity, precision: 12);
        Assert.Empty(events[1]!.AffectedRegions);
    }

    [Fact]
    public void Observe_DoesNotPreserveStaticHistoryForLocalAnimation()
    {
        var detector = new StaticShadeDetector(_options);
        var frames = Enumerable.Range(0, 5)
            .Select(index => Frame(MotionPatternTiles(columnOffset: 0, rowOffset: 0), index * 100))
            .Concat([
                Frame(MotionPatternTiles(columnOffset: 0, rowOffset: 0, includeTrackedBrightContent: false, localBrightIndex: 10), 500),
                Frame(MotionPatternTiles(columnOffset: 0, rowOffset: 0, includeTrackedBrightContent: false, localBrightIndex: 10), 600)
            ]);

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(400), events[0]!.Timestamp);
        Assert.Equal(0, events[1]!.RecommendedOpacity, precision: 12);
        Assert.DoesNotContain(events, result =>
            result!.RecommendedOpacity > 0 &&
            result.Timestamp < DateTimeOffset.UnixEpoch.AddMilliseconds(800) &&
            result.AffectedRegions.Any(region => region.X > 0.45 && region.Y > 0.45));
    }

    [Fact]
    public void Observe_ExpandsStaticShadeAcrossConnectedBrightSurface()
    {
        var detector = new StaticShadeDetector(_options);

        var events = StaticTileWithChangingBrightNeighborSequence(frameCount: 5)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var staticShade = Assert.Single(events);
        Assert.Equal(2, staticShade!.AffectedTileCount);
        var region = Assert.Single(staticShade.AffectedRegions);
        Assert.True(region.X <= 0);
        Assert.True(region.Right >= 0.50);
        Assert.True(region.Y <= 0);
        Assert.True(region.Bottom >= 0.25);
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
    public void Observe_DetectsBrightStaticTileFromHighPercentileExposure()
    {
        var detector = new StaticShadeDetector(_options);
        var events = LocalizedStaticSequence([0.65, 0.65, 0.65, 0.65, 0.65], percentile95: 0.90, brightPixelRatio: 0.10)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var staticShade = Assert.Single(events);
        Assert.True(staticShade!.ExposureScore > _options.MinimumExposureScore);
        Assert.Equal(1, staticShade.AffectedTileCount);
    }

    [Fact]
    public void Observe_IncreasesOpacityForHigherExposureScore()
    {
        var lowerExposure = FirstEvent(
            LocalizedStaticSequence([0.80, 0.80, 0.80, 0.80, 0.80, 0.80], brightPixelRatio: 0.55));
        var higherExposure = FirstEvent(
            LocalizedStaticSequence([0.92, 0.92, 0.92, 0.92, 0.92, 0.92, 0.92], brightPixelRatio: 1.0));

        Assert.True(higherExposure.ExposureScore > lowerExposure.ExposureScore);
        Assert.True(higherExposure.RecommendedOpacity > lowerExposure.RecommendedOpacity);
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
        int startMs = 0,
        double? percentile95 = null,
        double? brightPixelRatio = null,
        EffectRegion? signalBounds = null,
        int targetIndex = 0)
    {
        for (var index = 0; index < values.Count; index++)
        {
            yield return Frame(
                LocalizedTiles(values[index], percentile95 ?? values[index], brightPixelRatio, signalBounds, targetIndex),
                startMs + (index * stepMs));
        }
    }

    private static IEnumerable<FrameAnalysisResult> UniformStaticSequence(IReadOnlyList<double> values, int stepMs = 100)
    {
        for (var index = 0; index < values.Count; index++)
        {
            yield return Frame(UniformTiles(values[index]), index * stepMs);
        }
    }

    private static IEnumerable<FrameAnalysisResult> AdjacentStaticSequence(IReadOnlyList<double> values, int stepMs = 100)
    {
        for (var index = 0; index < values.Count; index++)
        {
            yield return Frame(AdjacentTiles(values[index]), index * stepMs);
        }
    }

    private static IEnumerable<FrameAnalysisResult> MovingNeutralContentWithStaticToolbar(int frameCount, int stepMs = 100)
    {
        const int toolbarTileIndex = 1;

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var tiles = Enumerable.Range(0, 16)
                .Select(tileIndex =>
                {
                    if (tileIndex == toolbarTileIndex)
                    {
                        return Tile(tileIndex, 0.82, 0.86, 0.70);
                    }

                    var neutralMovement = 0.24 + (((tileIndex + frameIndex) % 8) * 0.025);
                    return Tile(tileIndex, neutralMovement, neutralMovement, 0.02);
                })
                .ToArray();

            yield return Frame(new FrameTileSummary(4, 4, tiles), frameIndex * stepMs);
        }
    }

    private static IEnumerable<FrameAnalysisResult> StaticTileWithChangingBrightNeighborSequence(
        int frameCount,
        int stepMs = 100)
    {
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var neighborLuminance = frameIndex % 2 == 0 ? 0.72 : 0.80;
            var tiles = Enumerable.Range(0, 16)
                .Select(index => index switch
                {
                    0 => Tile(index, 0.80),
                    1 => Tile(index, neighborLuminance),
                    _ => Tile(index, 0.05)
                })
                .ToArray();

            yield return Frame(new FrameTileSummary(4, 4, tiles), frameIndex * stepMs);
        }
    }

    private static IEnumerable<FrameAnalysisResult> ConnectedSignalSurfaceSequence(int frameCount, int stepMs = 100)
    {
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var tiles = Enumerable.Range(0, 16)
                .Select(index => index switch
                {
                    0 => Tile(index, 0.78, 0.92, 0.46, new EffectRegion(0.06, 0.10, 0.18, 0.08)),
                    1 => Tile(index, 0.77, 0.91, 0.44, new EffectRegion(0.25, 0.10, 0.18, 0.08)),
                    _ => Tile(index, 0.05)
                })
                .ToArray();

            yield return Frame(new FrameTileSummary(4, 4, tiles), frameIndex * stepMs);
        }
    }

    private static FrameAnalysisResult Frame(FrameTileSummary summary, int timestampMs, int width = 16, int height = 9) =>
        new(
            new FrameMetadata(DateTimeOffset.UnixEpoch.AddMilliseconds(timestampMs), width, height, "test"),
            AverageLuminance: summary.Tiles.Average(tile => tile.AverageLuminance),
            PeakLuminance: summary.Tiles.Max(tile => tile.AverageLuminance),
            Percentile95Luminance: summary.Tiles.Max(tile => tile.Percentile95Luminance),
            BrightPixelRatio: summary.Tiles.Average(tile => tile.BrightPixelRatio),
            TileSummary: summary);

    private static StaticShadeEvent FirstEvent(IEnumerable<FrameAnalysisResult> frames)
    {
        var detector = new StaticShadeDetector(new StaticShadeOptions
        {
            MinimumStaticDuration = TimeSpan.FromMilliseconds(300),
            FullOpacityDuration = TimeSpan.FromMilliseconds(900),
            BrightTileAverageThreshold = 0.70,
            BrightTilePercentile95Threshold = 0.76,
            BrightTilePixelRatioThreshold = 0.50,
            MaximumAverageDelta = 0.02,
            MinimumAffectedTileRatio = 0.05,
            MaximumAffectedTileRatio = 0.50,
            MinimumExposureScore = 0.18,
            BaseOpacity = 0.10,
            MaximumOpacity = 0.30
        });

        return frames
            .Select(detector.Observe)
            .First(result => result is not null)!;
    }

    private static FrameTileSummary LocalizedTiles(
        double luminance,
        double percentile95,
        double? brightPixelRatio = null,
        EffectRegion? signalBounds = null,
        int targetIndex = 0)
    {
        var tiles = Enumerable.Range(0, 16)
            .Select(index => index == targetIndex
                ? Tile(index, luminance, percentile95, brightPixelRatio, signalBounds)
                : Tile(index, 0.05))
            .ToArray();

        return new FrameTileSummary(4, 4, tiles);
    }

    private static FrameTileSummary AdjacentTiles(double luminance)
    {
        int[] targets = [0, 1, 4, 5];
        var tiles = Enumerable.Range(0, 16)
            .Select(index => targets.Contains(index)
                ? Tile(index, luminance)
                : Tile(index, 0.05))
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

    private static FrameTileSummary MotionPatternTiles(
        int columnOffset,
        int rowOffset,
        bool includeTrackedBrightContent = true,
        int? localBrightIndex = null)
    {
        var tiles = Enumerable.Range(0, 16)
            .Select(index =>
            {
                var screenColumn = index % 4;
                var screenRow = index / 4;
                var contentColumn = screenColumn - columnOffset;
                var contentRow = screenRow - rowOffset;
                var isTrackedBrightContent = includeTrackedBrightContent &&
                    contentColumn == 1 &&
                    contentRow == 0;
                var isLocalBright = localBrightIndex == index;
                if (isTrackedBrightContent || isLocalBright)
                {
                    var signal = new EffectRegion(
                        (screenColumn / 4.0) + 0.06,
                        (screenRow / 4.0) + 0.07,
                        0.11,
                        0.10);
                    return Tile(index, 0.82, 0.92, 0.66, signal);
                }

                var luminance = ContentLuminance(contentColumn, contentRow);
                return Tile(index, luminance, luminance + 0.04, 0.02);
            })
            .ToArray();

        return new FrameTileSummary(4, 4, tiles);
    }

    private static FrameTileSummary LargeTrackedScrollTiles(
        int rowOffset,
        bool includeTrackedBrightContent = true,
        bool replacementAtShiftedTarget = false,
        bool disturbReplacementNeighborhood = false)
    {
        const int columns = 12;
        const int rows = 8;
        const int targetContentColumn = 4;
        const int targetContentRow = 2;
        var shiftedTargetColumn = targetContentColumn;
        var shiftedTargetRow = targetContentRow + rowOffset;
        var tiles = Enumerable.Range(0, columns * rows)
            .Select(index =>
            {
                var column = index % columns;
                var row = index / columns;
                var contentColumn = column;
                var contentRow = row - rowOffset;
                var isOriginalTrackedContent = includeTrackedBrightContent &&
                    contentColumn == targetContentColumn &&
                    contentRow == targetContentRow;
                var isReplacement = replacementAtShiftedTarget &&
                    column == shiftedTargetColumn &&
                    row == shiftedTargetRow;
                if (isOriginalTrackedContent || isReplacement)
                {
                    var signal = new EffectRegion(
                        (column / (double)columns) + 0.010,
                        (row / (double)rows) + 0.012,
                        0.040,
                        0.052);
                    return Tile(index, 0.82, 0.92, 0.66, signal, columns);
                }

                var nearReplacement = disturbReplacementNeighborhood &&
                    Math.Abs(column - shiftedTargetColumn) <= 1 &&
                    Math.Abs(row - shiftedTargetRow) <= 1;
                var luminance = nearReplacement
                    ? 0.31 + (((column + row) % 3) * 0.035)
                    : ContentLuminance(contentColumn, contentRow);
                return Tile(index, luminance, luminance + 0.04, 0.02, columns: columns);
            })
            .ToArray();

        return new FrameTileSummary(columns, rows, tiles);
    }

    private static FrameTileSummary PageScrollWithFixedToolbarTiles(int rowOffset)
    {
        const int columns = 32;
        const int rows = 18;
        var tiles = Enumerable.Range(0, columns * rows)
            .Select(index =>
            {
                var column = index % columns;
                var row = index / columns;
                if (row == 0)
                {
                    return Tile(index, 0.80, 0.88, 0.72, columns: columns);
                }

                var contentRow = row - rowOffset;
                var luminance = ContentLuminance(column, contentRow);
                return Tile(index, luminance, luminance + 0.04, 0.02, columns: columns);
            })
            .ToArray();

        return new FrameTileSummary(columns, rows, tiles);
    }

    private static FrameTileSummary RepeatedBrightListTiles(int scrollOffsetRows)
    {
        const int columns = 32;
        const int rows = 18;
        var tiles = Enumerable.Range(0, columns * rows)
            .Select(index =>
            {
                var column = index % columns;
                var row = index / columns;
                var contentRow = row - scrollOffsetRows;
                var isBrightCardColumn = column is >= 4 and <= 7;
                var isBrightCard = isBrightCardColumn && contentRow % 2 == 0;
                if (isBrightCard)
                {
                    var signal = new EffectRegion(
                        (column / (double)columns) + 0.006,
                        (row / (double)rows) + 0.010,
                        0.020,
                        0.032);
                    return Tile(index, 0.84, 0.91, 0.68, signal, columns);
                }

                var luminance = 0.055 + (((contentRow * 13 + column * 7) % 9) * 0.008);
                return Tile(index, luminance, luminance + 0.03, 0.02, columns: columns);
            })
            .ToArray();

        return new FrameTileSummary(columns, rows, tiles);
    }

    private static double ContentLuminance(int column, int row)
    {
        var bucket = Math.Abs(((row + 11) * 23) + ((column + 7) * 29) + ((row + 5) * (column + 3) * 7)) % 17;
        return 0.10 + (bucket * 0.025);
    }

    private static TileLuminanceStats Tile(
        int index,
        double luminance,
        double? percentile95 = null,
        double? brightPixelRatio = null,
        EffectRegion? signalBounds = null,
        int columns = 4)
    {
        var brightRatio = brightPixelRatio ?? (luminance >= 0.70 ? 1 : 0);
        return new TileLuminanceStats(
            Column: index % columns,
            Row: index / columns,
            AverageLuminance: luminance,
            Percentile95Luminance: percentile95 ?? luminance,
            BrightPixelRatio: brightRatio,
            SignalBounds: signalBounds);
    }
}
