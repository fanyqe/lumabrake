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
                if (duration >= Options.MinimumStaticDuration)
                {
                    affectedRegions.Add(TileRegion(tile, summary));
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

        var severity = StaticSeverity(longestStaticDuration);
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
            affectedRegions);
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
        tile.BrightPixelRatio >= Options.BrightTilePixelRatioThreshold;

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
