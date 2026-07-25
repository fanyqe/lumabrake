using LumaBrake.Analysis.Detection;
using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Tests;

public sealed class FrameTileMotionEstimatorTests
{
    [Fact]
    public void Estimate_ReturnsReliableDisplacementForOneRowScroll()
    {
        var previous = Pattern(offsetColumn: 0, offsetRow: 0);
        var current = Pattern(offsetColumn: 0, offsetRow: 1);

        var motion = FrameTileMotionEstimator.Estimate(previous, current);

        Assert.True(motion.IsReliable);
        Assert.Equal(0, motion.ColumnDelta);
        Assert.Equal(1, motion.RowDelta);
        Assert.True(motion.Confidence >= 0.58);
        Assert.False(motion.IsSceneChange);
    }

    [Fact]
    public void Estimate_ReturnsReliableDisplacementForOneColumnScroll()
    {
        var previous = Pattern(offsetColumn: 0, offsetRow: 0);
        var current = Pattern(offsetColumn: 1, offsetRow: 0);

        var motion = FrameTileMotionEstimator.Estimate(previous, current);

        Assert.True(motion.IsReliable);
        Assert.Equal(1, motion.ColumnDelta);
        Assert.Equal(0, motion.RowDelta);
        Assert.True(motion.Confidence >= 0.58);
        Assert.False(motion.IsSceneChange);
    }

    [Fact]
    public void Estimate_ReturnsUnreliableSceneChangeForLargeVisualJump()
    {
        var previous = Pattern(offsetColumn: 0, offsetRow: 0);
        var current = Uniform(0.88);

        var motion = FrameTileMotionEstimator.Estimate(previous, current);

        Assert.False(motion.IsReliable);
        Assert.True(motion.IsSceneChange);
        Assert.True(motion.Confidence < 0.58);
    }

    [Fact]
    public void Estimate_ReturnsLikelyMotionForRepeatedBrightListScroll()
    {
        var previous = RepeatedBrightList(scrollOffsetRows: 0);
        var current = RepeatedBrightList(scrollOffsetRows: 1);

        var motion = FrameTileMotionEstimator.Estimate(previous, current);

        Assert.True(motion.HasMotion);
        Assert.True(motion.IsMotionLikely);
        Assert.False(motion.IsSceneChange);
        Assert.True(motion.MatchedTileRatio >= 0.90);
    }

    [Fact]
    public void Estimate_DoesNotTreatSparseLocalizedFlashAsLikelyMotion()
    {
        var previous = SparseLocalizedFlash(bright: true);
        var current = SparseLocalizedFlash(bright: false);

        var motion = FrameTileMotionEstimator.Estimate(previous, current);

        Assert.False(motion.IsMotionLikely);
    }

    private static FrameTileSummary Pattern(int offsetColumn, int offsetRow)
    {
        var tiles = Enumerable.Range(0, 16)
            .Select(index =>
            {
                var column = index % 4;
                var row = index / 4;
                var luminance = ContentLuminance(column - offsetColumn, row - offsetRow);
                return Tile(index, luminance);
            })
            .ToArray();

        return new FrameTileSummary(4, 4, tiles);
    }

    private static FrameTileSummary Uniform(double luminance)
    {
        var tiles = Enumerable.Range(0, 16)
            .Select(index => Tile(index, luminance))
            .ToArray();

        return new FrameTileSummary(4, 4, tiles);
    }

    private static FrameTileSummary RepeatedBrightList(int scrollOffsetRows)
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
                    luminance + 0.03,
                    isBrightCard ? 1 : 0);
            })
            .ToArray();

        return new FrameTileSummary(columns, rows, tiles);
    }

    private static FrameTileSummary SparseLocalizedFlash(bool bright)
    {
        var tiles = Enumerable.Range(0, 16)
            .Select(index => Tile(index, index == 0 && bright ? 0.85 : 0.05))
            .ToArray();

        return new FrameTileSummary(4, 4, tiles);
    }

    private static double ContentLuminance(int column, int row)
    {
        var bucket = Math.Abs(((row + 13) * 23) + ((column + 5) * 31) + ((row + 3) * (column + 11) * 5)) % 19;
        return 0.08 + (bucket * 0.035);
    }

    private static TileLuminanceStats Tile(int index, double luminance, int columns = 4) =>
        new(
            Column: index % columns,
            Row: index / columns,
            AverageLuminance: luminance,
            Percentile95Luminance: luminance + 0.03,
            BrightPixelRatio: luminance >= 0.70 ? 0.68 : 0.02);
}
