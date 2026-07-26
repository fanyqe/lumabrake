using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Detection;

public sealed class StaticShadeDetector
{
    private readonly Dictionary<int, TileStaticState> _tiles = new();

    public StaticShadeDetector(StaticShadeOptions options)
    {
        Options = options;
    }

    public StaticShadeOptions Options { get; }

    public StaticShadeEvent? Observe(FrameAnalysisResult frame)
    {
        if (!Options.Enabled)
        {
            return null;
        }

        var summary = frame.TileSummary;
        if (summary is null || summary.Tiles.Count == 0)
        {
            return null;
        }

        RemoveStaleTileStates(summary.Tiles.Count);

        var affectedRegions = new List<EffectRegion>();
        var longestStaticDuration = TimeSpan.Zero;
        var exposureScoreSum = 0.0;

        for (var index = 0; index < summary.Tiles.Count; index++)
        {
            var tile = summary.Tiles[index];
            var state = StateFor(index, tile.AverageLuminance, frame.Metadata.CapturedAt);
            var isBright = IsBright(tile);
            var frameGap = frame.Metadata.CapturedAt - state.LastSeen;
            if (frameGap < TimeSpan.Zero || frameGap > Options.MaximumFrameGap)
            {
                state.StaticSince = isBright ? frame.Metadata.CapturedAt : null;
                state.PreviousAverage = tile.AverageLuminance;
                state.LastSeen = frame.Metadata.CapturedAt;
                continue;
            }

            var averageDelta = Math.Abs(tile.AverageLuminance - state.PreviousAverage);

            if (isBright && averageDelta <= Options.MaximumAverageDelta)
            {
                state.StaticSince ??= state.LastSeen;
                var duration = frame.Metadata.CapturedAt - state.StaticSince.Value;
                var exposureScore = ExposureScore(tile, duration);
                if (duration >= Options.MinimumStaticDuration && exposureScore >= Options.MinimumExposureScore)
                {
                    affectedRegions.Add(TileRegion(tile, summary));
                    exposureScoreSum += exposureScore;
                    longestStaticDuration = duration > longestStaticDuration ? duration : longestStaticDuration;
                }
            }
            else
            {
                state.StaticSince = isBright ? frame.Metadata.CapturedAt : null;
            }

            state.PreviousAverage = tile.AverageLuminance;
            state.LastSeen = frame.Metadata.CapturedAt;
        }

        if (affectedRegions.Count == 0)
        {
            return null;
        }

        var affectedRatio = affectedRegions.Count / (double)summary.Tiles.Count;
        if (affectedRatio < Options.MinimumAffectedTileRatio ||
            affectedRatio > Options.MaximumAffectedTileRatio)
        {
            return null;
        }

        var exposureSeverity = exposureScoreSum / affectedRegions.Count;
        var severity = Math.Max(StaticSeverity(longestStaticDuration), exposureSeverity);
        var opacity = Math.Clamp(
            Options.BaseOpacity + (severity * (Options.MaximumOpacity - Options.BaseOpacity)),
            0,
            Options.MaximumOpacity);

        return new StaticShadeEvent(
            frame.Metadata.CapturedAt,
            affectedRegions.Count,
            summary.Tiles.Count,
            affectedRatio,
            longestStaticDuration,
            opacity,
            affectedRegions,
            exposureSeverity);
    }

    private void RemoveStaleTileStates(int currentTileCount)
    {
        foreach (var removedIndex in _tiles.Keys.Where(index => index >= currentTileCount).ToArray())
        {
            _tiles.Remove(removedIndex);
        }
    }

    private TileStaticState StateFor(int index, double average, DateTimeOffset timestamp)
    {
        if (_tiles.TryGetValue(index, out var state))
        {
            return state;
        }

        state = new TileStaticState(average, timestamp);
        _tiles[index] = state;
        return state;
    }

    private bool IsBright(TileLuminanceStats tile) =>
        tile.AverageLuminance >= Options.BrightTileAverageThreshold ||
        tile.Percentile95Luminance >= Options.BrightTilePercentile95Threshold ||
        tile.BrightPixelRatio >= Options.BrightTilePixelRatioThreshold;

    private double ExposureScore(TileLuminanceStats tile, TimeSpan duration)
    {
        var luminanceScore = Math.Max(
            NormalizedAboveThreshold(tile.AverageLuminance, Options.BrightTileAverageThreshold),
            NormalizedAboveThreshold(tile.Percentile95Luminance, Options.BrightTilePercentile95Threshold));
        var brightPixelScore = NormalizedAboveThreshold(tile.BrightPixelRatio, Options.BrightTilePixelRatioThreshold);
        var durationScore = StaticSeverity(duration);

        var luminanceWeight = PositiveWeight(Options.ExposureLuminanceWeight);
        var brightPixelWeight = PositiveWeight(Options.ExposureBrightPixelWeight);
        var durationWeight = PositiveWeight(Options.ExposureDurationWeight);
        var weightSum = luminanceWeight + brightPixelWeight + durationWeight;
        if (weightSum <= 0)
        {
            return Math.Max(luminanceScore, Math.Max(brightPixelScore, durationScore));
        }

        return Math.Clamp(
            ((luminanceScore * luminanceWeight) +
            (brightPixelScore * brightPixelWeight) +
            (durationScore * durationWeight)) /
            weightSum,
            0,
            1);
    }

    private double StaticSeverity(TimeSpan duration)
    {
        if (Options.FullOpacityDuration <= Options.MinimumStaticDuration)
        {
            return 1;
        }

        return Math.Clamp(
            (duration - Options.MinimumStaticDuration).TotalMilliseconds /
            (Options.FullOpacityDuration - Options.MinimumStaticDuration).TotalMilliseconds,
            0,
            1);
    }

    private static double NormalizedAboveThreshold(double value, double threshold)
    {
        if (threshold >= 1)
        {
            return value >= threshold ? 1 : 0;
        }

        return Math.Clamp((value - threshold) / (1 - threshold), 0, 1);
    }

    private static double PositiveWeight(double weight) => Math.Max(0, weight);

    private static EffectRegion TileRegion(TileLuminanceStats tile, FrameTileSummary summary)
    {
        var width = 1.0 / summary.Columns;
        var height = 1.0 / summary.Rows;
        return new EffectRegion(tile.Column * width, tile.Row * height, width, height);
    }

    private sealed class TileStaticState
    {
        public TileStaticState(double previousAverage, DateTimeOffset lastSeen)
        {
            PreviousAverage = previousAverage;
            LastSeen = lastSeen;
        }

        public double PreviousAverage { get; set; }

        public DateTimeOffset LastSeen { get; set; }

        public DateTimeOffset? StaticSince { get; set; }
    }
}
