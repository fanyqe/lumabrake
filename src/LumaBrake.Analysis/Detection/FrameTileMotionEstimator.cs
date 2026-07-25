using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Detection;

internal static class FrameTileMotionEstimator
{
    public static FrameTileMotion Estimate(
        FrameTileSummary previous,
        FrameTileSummary current,
        FrameTileMotionOptions? options = null)
    {
        if (previous.Columns != current.Columns ||
            previous.Rows != current.Rows ||
            previous.Tiles.Count != current.Tiles.Count ||
            previous.Tiles.Count == 0)
        {
            return FrameTileMotion.Unreliable;
        }

        var normalized = options ?? FrameTileMotionOptions.Default;
        var maxColumnDelta = Math.Min(Math.Max(0, normalized.MaximumColumnDisplacement), current.Columns - 1);
        var maxRowDelta = Math.Min(Math.Max(0, normalized.MaximumRowDisplacement), current.Rows - 1);
        MotionCandidate? best = null;
        MotionCandidate? second = null;
        MotionCandidate? zero = null;

        for (var rowDelta = -maxRowDelta; rowDelta <= maxRowDelta; rowDelta++)
        {
            for (var columnDelta = -maxColumnDelta; columnDelta <= maxColumnDelta; columnDelta++)
            {
                var candidate = Score(previous, current, columnDelta, rowDelta);
                if (candidate.MatchedTileRatio < normalized.MinimumMatchedTileRatio)
                {
                    continue;
                }

                if (candidate.ColumnDelta == 0 && candidate.RowDelta == 0)
                {
                    zero = candidate;
                }

                if (best is null || candidate.RankScore < best.RankScore)
                {
                    second = best;
                    best = candidate;
                    continue;
                }

                if (second is null || candidate.RankScore < second.RankScore)
                {
                    second = candidate;
                }
            }
        }

        if (best is null)
        {
            return FrameTileMotion.Unreliable;
        }

        var separation = Math.Max(0, (second?.RankScore ?? 1) - best.RankScore);
        var zeroMotionSeparation = Math.Max(0, (zero?.MeanError ?? 1) - best.MeanError);
        var accuracy = 1 - Math.Clamp(best.MeanError / normalized.ReliableMeanError, 0, 1);
        var distinctness = Math.Clamp(separation / normalized.ReliableSeparation, 0, 1);
        var overlap = Math.Clamp(best.MatchedTileRatio / normalized.MinimumMatchedTileRatio, 0, 1);
        var confidence = Math.Clamp(((accuracy * 0.65) + (distinctness * 0.35)) * overlap, 0, 1);
        var reliable = confidence >= normalized.MinimumConfidence &&
            best.MeanError <= normalized.ReliableMeanError &&
            separation >= normalized.MinimumSeparation;
        var sceneChange = best.MeanError >= normalized.SceneChangeMeanError && confidence < normalized.MinimumConfidence;
        var motionLikely = best.HasMotion &&
            !sceneChange &&
            best.MatchedTileRatio >= normalized.MotionLikelyMinimumMatchedTileRatio &&
            best.MeanError <= normalized.MotionLikelyMeanError &&
            zeroMotionSeparation >= normalized.MotionLikelyZeroSeparation;

        return new FrameTileMotion(
            best.ColumnDelta,
            best.RowDelta,
            best.MeanError,
            best.MatchedTileRatio,
            confidence,
            reliable,
            sceneChange,
            motionLikely);
    }

    private static MotionCandidate Score(
        FrameTileSummary previous,
        FrameTileSummary current,
        int columnDelta,
        int rowDelta)
    {
        var weightedError = 0.0;
        var weightSum = 0.0;
        var matched = 0;

        for (var row = 0; row < current.Rows; row++)
        {
            var previousRow = row - rowDelta;
            if (previousRow < 0 || previousRow >= previous.Rows)
            {
                continue;
            }

            for (var column = 0; column < current.Columns; column++)
            {
                var previousColumn = column - columnDelta;
                if (previousColumn < 0 || previousColumn >= previous.Columns)
                {
                    continue;
                }

                var previousTile = previous.Tiles[(previousRow * previous.Columns) + previousColumn];
                var currentTile = current.Tiles[(row * current.Columns) + column];
                var error =
                    (Math.Abs(previousTile.AverageLuminance - currentTile.AverageLuminance) * 0.62) +
                    (Math.Abs(previousTile.Percentile95Luminance - currentTile.Percentile95Luminance) * 0.26) +
                    (Math.Abs(previousTile.BrightPixelRatio - currentTile.BrightPixelRatio) * 0.12);
                var weight = 0.7 +
                    (Math.Max(previousTile.BrightPixelRatio, currentTile.BrightPixelRatio) * 0.5) +
                    (Math.Abs(previousTile.AverageLuminance - currentTile.AverageLuminance) * 0.2);

                weightedError += error * weight;
                weightSum += weight;
                matched++;
            }
        }

        var matchedTileRatio = matched / (double)current.Tiles.Count;
        var meanError = weightSum <= 0 ? 1 : weightedError / weightSum;
        var displacementPenalty = (Math.Abs(columnDelta) + Math.Abs(rowDelta)) * 0.0015;
        return new MotionCandidate(columnDelta, rowDelta, meanError, matchedTileRatio, meanError + displacementPenalty);
    }

    private sealed record MotionCandidate(
        int ColumnDelta,
        int RowDelta,
        double MeanError,
        double MatchedTileRatio,
        double RankScore)
    {
        public bool HasMotion => ColumnDelta != 0 || RowDelta != 0;
    }
}

internal sealed record FrameTileMotion(
    int ColumnDelta,
    int RowDelta,
    double MeanError,
    double MatchedTileRatio,
    double Confidence,
    bool IsReliable,
    bool IsSceneChange,
    bool IsMotionLikely)
{
    public static FrameTileMotion Unreliable { get; } = new(0, 0, 1, 0, 0, false, true, false);

    public bool HasMotion => ColumnDelta != 0 || RowDelta != 0;
}

internal sealed record FrameTileMotionOptions
{
    public static FrameTileMotionOptions Default { get; } = new();

    public int MaximumColumnDisplacement { get; init; } = 4;

    public int MaximumRowDisplacement { get; init; } = 6;

    public double MinimumMatchedTileRatio { get; init; } = 0.45;

    public double ReliableMeanError { get; init; } = 0.055;

    public double SceneChangeMeanError { get; init; } = 0.18;

    public double MinimumSeparation { get; init; } = 0.012;

    public double ReliableSeparation { get; init; } = 0.05;

    public double MinimumConfidence { get; init; } = 0.58;

    public double MotionLikelyMeanError { get; init; } = 0.075;

    public double MotionLikelyZeroSeparation { get; init; } = 0.035;

    public double MotionLikelyMinimumMatchedTileRatio { get; init; } = 0.90;
}
