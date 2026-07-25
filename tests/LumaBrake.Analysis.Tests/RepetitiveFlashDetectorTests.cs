using LumaBrake.Analysis.Detection;
using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Effects;
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
    public void Observe_DetectsBroadGrayToWhiteRepetitiveFlash()
    {
        var detector = new RepetitiveFlashDetector(_options);
        var events = UniformSequence([0.30, 0.85, 0.30, 0.85])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var flash = Assert.Single(events);
        Assert.Equal(16, flash!.AffectedTileCount);
        Assert.Equal(1, flash.AffectedTileRatio, precision: 12);
    }

    [Fact]
    public void Observe_DefaultOptionsDetectOnThirdTransition()
    {
        var detector = new RepetitiveFlashDetector(new FlashDetectorOptions());
        var events = UniformSequence([0.05, 0.85, 0.05, 0.85], stepMs: 50)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var flash = Assert.Single(events);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(150), flash!.Timestamp);
        Assert.Equal(3, flash.MaxTransitionCount);
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
    public void Observe_DoesNotTriggerWhenDisabled()
    {
        var detector = new RepetitiveFlashDetector(_options with { Enabled = false });
        var events = UniformSequence([0.05, 0.85, 0.05, 0.85])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_DetectsLocalizedTileFlash()
    {
        var detector = new RepetitiveFlashDetector(_options);
        var events = LocalizedSequence([0.05, 0.85, 0.05, 0.85, 0.05])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var flash = Assert.Single(events);
        Assert.Equal(1, flash!.AffectedTileCount);
        Assert.Equal(0.0625, flash.AffectedTileRatio, precision: 12);
        var region = Assert.Single(flash.AffectedRegions);
        Assert.Equal(0, region.X, precision: 12);
        Assert.Equal(0, region.Y, precision: 12);
        Assert.True(region.Width > 0.25);
        Assert.True(region.Height > 0.25);
    }

    [Fact]
    public void Observe_DetectsLocalizedFlashOverNoisyMovingBackground()
    {
        var detector = new RepetitiveFlashDetector(_options);
        var events = NoisyLocalizedSequence([false, true, false, true, false])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var flash = Assert.Single(events);
        Assert.False(flash!.IsRedDominant);
        Assert.Equal(1, flash.AffectedTileCount);
        Assert.Equal(0.0625, flash.AffectedTileRatio, precision: 12);
        var region = Assert.Single(flash.AffectedRegions);
        Assert.True(region.X <= 0.25);
        Assert.True(region.Y <= 0.25);
        Assert.True(region.Right >= 0.50);
        Assert.True(region.Bottom >= 0.50);
    }

    [Fact]
    public void Observe_MergesAdjacentLocalizedFlashTilesIntoOneMask()
    {
        var detector = new RepetitiveFlashDetector(_options);
        var events = AdjacentLocalizedSequence([0.05, 0.85, 0.05, 0.85, 0.05])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var flash = Assert.Single(events);
        Assert.Equal(4, flash!.AffectedTileCount);
        var region = Assert.Single(flash.AffectedRegions);
        Assert.True(region.X <= 0);
        Assert.True(region.Y <= 0);
        Assert.True(region.Right >= 0.50);
        Assert.True(region.Bottom >= 0.50);
    }

    [Fact]
    public void Observe_ExpandsLocalizedMaskAcrossConnectedBrightSurface()
    {
        var detector = new RepetitiveFlashDetector(_options);
        var events = WideBrightSurfaceWithFlashingEdgeSequence([0.05, 0.85, 0.05, 0.85, 0.05])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var flash = Assert.Single(events);
        var region = Assert.Single(flash!.AffectedRegions);
        Assert.True(region.X <= 0);
        Assert.True(region.Y <= 0);
        Assert.True(region.Right >= 0.75);
        Assert.True(region.Bottom >= 0.25);
    }

    [Fact]
    public void Observe_DefaultOptionsDetectSmallLocalizedFlashAfterRegionalConfirmation()
    {
        var detector = new RepetitiveFlashDetector(new FlashDetectorOptions());
        var events = LocalizedWideGridSequence([0.05, 0.85, 0.05, 0.85, 0.05], stepMs: 50)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var flash = Assert.Single(events);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(200), flash!.Timestamp);
        Assert.True(flash.MaxTransitionCount >= 3);
        Assert.Equal(1, flash.AffectedTileCount);
        Assert.Equal(1 / 576.0, flash.AffectedTileRatio, precision: 12);
        Assert.Single(flash.AffectedRegions);
    }

    [Fact]
    public void Observe_DefaultOptionsDoNotCreateLocalizedMaskForOneScrollLikeBrightReveal()
    {
        var detector = new RepetitiveFlashDetector(new FlashDetectorOptions());
        var events = LocalizedWideGridSequence([0.05, 0.85, 0.05], stepMs: 50)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_DoesNotTreatBrightPageScrollAsFlash()
    {
        var detector = new RepetitiveFlashDetector(_options);
        var events = BroadBrightPageScrollSequence([0.34, 0.78, 0.34, 0.78])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_DoesNotTreatTileCorrelatedVerticalScrollAsLocalizedFlash()
    {
        var detector = new RepetitiveFlashDetector(new FlashDetectorOptions
        {
            Window = TimeSpan.FromMilliseconds(1000),
            MinimumTransitions = 3,
            MinimumAffectedTileRatio = 0.0015,
            Cooldown = TimeSpan.Zero
        });

        var events = TexturedVerticalScrollSequence(frameCount: 8)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_DoesNotCountSlowAlternatingPageChangesAsFlash()
    {
        var detector = new RepetitiveFlashDetector(new FlashDetectorOptions
        {
            Window = TimeSpan.FromMilliseconds(2500),
            MaximumTransitionInterval = TimeSpan.FromMilliseconds(550),
            MinimumTransitions = 3,
            MinimumAffectedTileRatio = 0.05,
            Cooldown = TimeSpan.Zero
        });

        var events = UniformSequence([0.05, 0.85, 0.05, 0.85, 0.05], stepMs: 700)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_CountsFastTransitionsWithinMaximumTransitionInterval()
    {
        var detector = new RepetitiveFlashDetector(new FlashDetectorOptions
        {
            Window = TimeSpan.FromMilliseconds(1200),
            MaximumTransitionInterval = TimeSpan.FromMilliseconds(550),
            MinimumTransitions = 3,
            MinimumAffectedTileRatio = 0.05,
            Cooldown = TimeSpan.Zero
        });

        var events = UniformSequence([0.05, 0.85, 0.05, 0.85], stepMs: 150)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Single(events);
    }

    [Fact]
    public void Observe_DoesNotCarryFlashHistoryToScrolledReplacementWithDifferentContext()
    {
        var detector = new RepetitiveFlashDetector(new FlashDetectorOptions
        {
            Window = TimeSpan.FromMilliseconds(1000),
            MinimumTransitions = 3,
            EarlyLocalizedTransitions = 3,
            MinimumAffectedTileRatio = 0.0015,
            Cooldown = TimeSpan.Zero
        });
        var previous = MovingFlashingPanelTiles(rowOffset: 0, bright: false);
        var replacement = MovingFlashingPanelTiles(
            rowOffset: 1,
            bright: true,
            includeTrackedPanel: false,
            replacementAtShiftedTarget: true,
            disturbReplacementNeighborhood: true);
        var motion = FrameTileMotionEstimator.Estimate(previous, replacement);
        var frames = new[]
        {
            Frame(0.05, MovingFlashingPanelTiles(rowOffset: 0, bright: false), 0),
            Frame(0.12, MovingFlashingPanelTiles(rowOffset: 0, bright: true), 80),
            Frame(0.05, MovingFlashingPanelTiles(rowOffset: 0, bright: false), 160),
            Frame(0.12, replacement, 240),
            Frame(0.05, MovingFlashingPanelTiles(
                rowOffset: 1,
                bright: false,
                includeTrackedPanel: false,
                replacementAtShiftedTarget: true,
                disturbReplacementNeighborhood: true), 320)
        };

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.True(motion.HasMotion, motion.ToString());
        Assert.True(motion.IsReliable || motion.IsMotionLikely, motion.ToString());
        Assert.Empty(events);
    }

    [Fact]
    public void Observe_CarriesFlashHistoryWithSameContentDuringScroll()
    {
        var detector = new RepetitiveFlashDetector(new FlashDetectorOptions
        {
            Window = TimeSpan.FromMilliseconds(1000),
            MinimumTransitions = 3,
            EarlyLocalizedTransitions = 3,
            MinimumAffectedTileRatio = 0.0015,
            Cooldown = TimeSpan.Zero
        });
        var previous = MovingFlashingPanelTiles(rowOffset: 0, bright: false);
        var current = MovingFlashingPanelTiles(rowOffset: 1, bright: true);
        var motion = FrameTileMotionEstimator.Estimate(previous, current);
        var frames = new[]
        {
            Frame(0.05, MovingFlashingPanelTiles(rowOffset: 0, bright: false), 0),
            Frame(0.12, MovingFlashingPanelTiles(rowOffset: 0, bright: true), 80),
            Frame(0.05, MovingFlashingPanelTiles(rowOffset: 0, bright: false), 160),
            Frame(0.12, current, 240),
            Frame(0.05, MovingFlashingPanelTiles(rowOffset: 1, bright: false), 320),
            Frame(0.12, MovingFlashingPanelTiles(rowOffset: 1, bright: true), 400)
        };

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.True(motion.HasMotion, motion.ToString());
        Assert.True(motion.IsReliable || motion.IsMotionLikely, motion.ToString());
        var flash = Assert.Single(events);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(320), flash!.Timestamp);
        Assert.True(flash.MaxTransitionCount >= 4);
        Assert.Single(flash.AffectedRegions);
    }

    [Fact]
    public void Observe_DetectsLocalizedNeutralBrightFlash()
    {
        var detector = new RepetitiveFlashDetector(new FlashDetectorOptions());
        var events = LocalizedWideGridSequence([0.30, 0.85, 0.30, 0.85, 0.30], stepMs: 50)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var flash = Assert.Single(events);
        Assert.True(flash!.MaxTransitionCount >= 3);
        Assert.Equal(1, flash.AffectedTileCount);
    }

    [Fact]
    public void Observe_UsesLastBrightSignalBoundsWhenActivationOccursOnDarkFrame()
    {
        var detector = new RepetitiveFlashDetector(new FlashDetectorOptions());
        var signal = new EffectRegion(0.006, 0.012, 0.018, 0.026);
        var events = LocalizedSignalSequence([0.05, 0.85, 0.05, 0.85, 0.05], signal, stepMs: 50)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var flash = Assert.Single(events);
        var region = Assert.Single(flash!.AffectedRegions);
        Assert.True(region.X <= signal.X);
        Assert.True(region.Y <= signal.Y);
        Assert.True(region.Right >= signal.Right - 0.0001);
        Assert.True(region.Bottom >= signal.Bottom - 0.0001);
    }

    [Fact]
    public void Observe_DoesNotCreateLocalizedMaskForTinySignalNoise()
    {
        var detector = new RepetitiveFlashDetector(new FlashDetectorOptions());
        var tinySignal = new EffectRegion(0.002, 0.002, 0.002, 0.002);
        var events = LocalizedSignalSequence([0.05, 0.85, 0.05], tinySignal, stepMs: 50)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_DetectsRedDominantFlashBelowBrightLuminanceThreshold()
    {
        var detector = new RepetitiveFlashDetector(_options);
        var events = RedDominantSequence([false, true, false, true])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var flash = Assert.Single(events);
        Assert.True(flash!.IsRedDominant);
        Assert.Equal(16, flash.RedDominantTileCount);
        Assert.Equal(16, flash.AffectedTileCount);
        Assert.InRange(flash.RecommendedOpacity, 0.45, _options.MaximumOpacity);
    }

    [Fact]
    public void Observe_DoesNotDetectRedFlashWhenRedDetectionIsDisabled()
    {
        var detector = new RepetitiveFlashDetector(_options with { RedFlashDetectionEnabled = false });
        var events = RedDominantSequence([false, true, false, true])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_DoesNotTriggerOnSteadyRedDominantTiles()
    {
        var detector = new RepetitiveFlashDetector(_options);
        var events = RedDominantSequence([true, true, true, true])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_DoesNotTreatWeakRedDominanceAsFlash()
    {
        var detector = new RepetitiveFlashDetector(_options);
        var events = WeakRedSequence([false, true, false, true])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
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

    private static IEnumerable<FrameAnalysisResult> RedDominantSequence(IReadOnlyList<bool> redFrames, int stepMs = 100)
    {
        for (var index = 0; index < redFrames.Count; index++)
        {
            var summary = redFrames[index] ? UniformRedTiles() : UniformTiles(0.05);
            yield return Frame(redFrames[index] ? 0.2126 : 0.05, summary, index * stepMs);
        }
    }

    private static IEnumerable<FrameAnalysisResult> WeakRedSequence(IReadOnlyList<bool> redFrames, int stepMs = 100)
    {
        for (var index = 0; index < redFrames.Count; index++)
        {
            var summary = redFrames[index] ? UniformWeakRedTiles() : UniformTiles(0.05);
            yield return Frame(redFrames[index] ? 0.20 : 0.05, summary, index * stepMs);
        }
    }

    private static IEnumerable<FrameAnalysisResult> LocalizedSequence(IReadOnlyList<double> values, int stepMs = 100)
    {
        for (var index = 0; index < values.Count; index++)
        {
            yield return Frame(values[index] / 16.0, LocalizedTiles(values[index]), index * stepMs);
        }
    }

    private static IEnumerable<FrameAnalysisResult> AdjacentLocalizedSequence(IReadOnlyList<double> values, int stepMs = 100)
    {
        for (var index = 0; index < values.Count; index++)
        {
            yield return Frame(values[index] / 4.0, AdjacentLocalizedTiles(values[index]), index * stepMs);
        }
    }

    private static IEnumerable<FrameAnalysisResult> WideBrightSurfaceWithFlashingEdgeSequence(
        IReadOnlyList<double> values,
        int stepMs = 100)
    {
        for (var index = 0; index < values.Count; index++)
        {
            yield return Frame(0.20, WideBrightSurfaceWithFlashingEdgeTiles(values[index]), index * stepMs);
        }
    }

    private static IEnumerable<FrameAnalysisResult> LocalizedWideGridSequence(IReadOnlyList<double> values, int stepMs = 100)
    {
        for (var index = 0; index < values.Count; index++)
        {
            yield return Frame(values[index] / 576.0, LocalizedWideGridTiles(values[index]), index * stepMs);
        }
    }

    private static IEnumerable<FrameAnalysisResult> LocalizedSignalSequence(
        IReadOnlyList<double> values,
        EffectRegion signal,
        int stepMs = 100)
    {
        for (var index = 0; index < values.Count; index++)
        {
            var signalBounds = values[index] >= 0.62 ? signal : null;
            yield return Frame(values[index] / 576.0, LocalizedWideGridTiles(values[index], signalBounds), index * stepMs);
        }
    }

    private static IEnumerable<FrameAnalysisResult> BroadBrightPageScrollSequence(
        IReadOnlyList<double> values,
        int stepMs = 100)
    {
        for (var index = 0; index < values.Count; index++)
        {
            var summary = UniformTiles(values[index]);
            yield return new FrameAnalysisResult(
                new FrameMetadata(DateTimeOffset.UnixEpoch.AddMilliseconds(index * stepMs), 16, 9, "test"),
                AverageLuminance: Math.Max(0.32, values[index] * 0.46),
                PeakLuminance: summary.Tiles.Max(tile => tile.AverageLuminance),
                Percentile95Luminance: values[index] >= 0.62 ? values[index] : 0.66,
                BrightPixelRatio: values[index] >= 0.62 ? 0.56 : 0.38,
                TileSummary: summary);
        }
    }

    private static IEnumerable<FrameAnalysisResult> TexturedVerticalScrollSequence(int frameCount, int stepMs = 80)
    {
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var summary = TexturedVerticalScrollTiles(frameIndex);
            yield return Frame(
                summary.Tiles.Average(tile => tile.AverageLuminance),
                summary,
                frameIndex * stepMs);
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
                return Tile(index, luminance, columns);
            })
            .ToArray();

        return new FrameTileSummary(columns, rows, tiles);
    }

    private static FrameTileSummary MovingFlashingPanelTiles(
        int rowOffset,
        bool bright,
        bool includeTrackedPanel = true,
        bool replacementAtShiftedTarget = false,
        bool disturbReplacementNeighborhood = false)
    {
        const int columns = 32;
        const int rows = 18;
        const int targetContentColumn = 7;
        const int targetContentRow = 5;
        var shiftedTargetColumn = targetContentColumn;
        var shiftedTargetRow = targetContentRow + rowOffset;
        var tiles = Enumerable.Range(0, columns * rows)
            .Select(index =>
            {
                var column = index % columns;
                var row = index / columns;
                var contentColumn = column;
                var contentRow = row - rowOffset;
                var isTrackedPanel = includeTrackedPanel &&
                    contentColumn == targetContentColumn &&
                    contentRow == targetContentRow;
                var isReplacement = replacementAtShiftedTarget &&
                    column == shiftedTargetColumn &&
                    row == shiftedTargetRow;
                if (isTrackedPanel || isReplacement)
                {
                    var signal = bright
                        ? new EffectRegion(
                            (column / (double)columns) + 0.006,
                            (row / (double)rows) + 0.010,
                            0.020,
                            0.032)
                        : null;
                    return Tile(index, bright ? 0.86 : 0.06, columns, signal);
                }

                var nearReplacement = disturbReplacementNeighborhood &&
                    Math.Abs(column - shiftedTargetColumn) <= 1 &&
                    Math.Abs(row - shiftedTargetRow) <= 1;
                var texture = nearReplacement
                    ? 0.54 + (((column + row) % 3) * 0.020)
                    : 0.045 + ((Math.Abs(((contentRow + 19) * 37) + ((column + 11) * 17) + ((contentRow + 3) * (column + 5) * 11)) % 10) * 0.045);
                return Tile(index, texture, columns);
            })
            .ToArray();

        return new FrameTileSummary(columns, rows, tiles);
    }

    private static IEnumerable<FrameAnalysisResult> NoisyLocalizedSequence(IReadOnlyList<bool> flashingFrames, int stepMs = 100)
    {
        const int flashingTileIndex = 5;

        for (var frameIndex = 0; frameIndex < flashingFrames.Count; frameIndex++)
        {
            var tiles = Enumerable.Range(0, 16)
                .Select(tileIndex =>
                {
                    if (tileIndex == flashingTileIndex)
                    {
                        return Tile(tileIndex, flashingFrames[frameIndex] ? 0.88 : 0.08);
                    }

                    var neutralMovement = 0.28 + (((tileIndex + frameIndex) % 5) * 0.025);
                    return Tile(tileIndex, neutralMovement);
                })
                .ToArray();
            var summary = new FrameTileSummary(4, 4, tiles);

            yield return Frame(tiles.Average(tile => tile.AverageLuminance), summary, frameIndex * stepMs);
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

    private static FrameTileSummary UniformRedTiles()
    {
        var tiles = Enumerable.Range(0, 16)
            .Select(index => RedTile(index))
            .ToArray();

        return new FrameTileSummary(4, 4, tiles);
    }

    private static FrameTileSummary UniformWeakRedTiles()
    {
        var tiles = Enumerable.Range(0, 16)
            .Select(index => WeakRedTile(index))
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

    private static FrameTileSummary AdjacentLocalizedTiles(double luminance)
    {
        int[] targets = [0, 1, 4, 5];
        var tiles = Enumerable.Range(0, 16)
            .Select(index => Tile(index, targets.Contains(index) ? luminance : 0.05))
            .ToArray();

        return new FrameTileSummary(4, 4, tiles);
    }

    private static FrameTileSummary WideBrightSurfaceWithFlashingEdgeTiles(double flashingLuminance)
    {
        int[] brightSurface = [0, 1, 2];
        var tiles = Enumerable.Range(0, 16)
            .Select(index =>
            {
                if (index == 0)
                {
                    return Tile(index, flashingLuminance);
                }

                return Tile(index, brightSurface.Contains(index) ? 0.86 : 0.05);
            })
            .ToArray();

        return new FrameTileSummary(4, 4, tiles);
    }

    private static FrameTileSummary LocalizedWideGridTiles(double luminance, EffectRegion? signalBounds = null)
    {
        var tiles = Enumerable.Range(0, 576)
            .Select(index => Tile(index, index == 0 ? luminance : 0.05, columns: 32, signalBounds: index == 0 ? signalBounds : null))
            .ToArray();

        return new FrameTileSummary(32, 18, tiles);
    }

    private static TileLuminanceStats Tile(
        int index,
        double luminance,
        int columns = 4,
        EffectRegion? signalBounds = null)
    {
        var brightRatio = luminance >= 0.62 ? 1 : 0;
        return new TileLuminanceStats(
            Column: index % columns,
            Row: index / columns,
            AverageLuminance: luminance,
            Percentile95Luminance: luminance,
            BrightPixelRatio: brightRatio,
            SignalBounds: signalBounds);
    }

    private static TileLuminanceStats RedTile(int index)
    {
        return new TileLuminanceStats(
            Column: index % 4,
            Row: index / 4,
            AverageLuminance: 0.2126,
            Percentile95Luminance: 0.2126,
            BrightPixelRatio: 0,
            AverageRedDominance: 1,
            RedDominantPixelRatio: 1);
    }

    private static TileLuminanceStats WeakRedTile(int index)
    {
        return new TileLuminanceStats(
            Column: index % 4,
            Row: index / 4,
            AverageLuminance: 0.20,
            Percentile95Luminance: 0.20,
            BrightPixelRatio: 0,
            AverageRedDominance: 0.20,
            RedDominantPixelRatio: 1);
    }
}
