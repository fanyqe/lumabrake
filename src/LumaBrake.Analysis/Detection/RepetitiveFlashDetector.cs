using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Detection;

public sealed class RepetitiveFlashDetector
{
    private readonly Dictionary<int, TileFlashState> _tiles = new();
    private DateTimeOffset _lastActivation = DateTimeOffset.MinValue;

    public RepetitiveFlashDetector(FlashDetectorOptions options)
    {
        Options = options;
    }

    public FlashDetectorOptions Options { get; }

    public FlashEvent? Observe(FrameAnalysisResult frame)
    {
        var summary = frame.TileSummary;
        if (summary is null || summary.Tiles.Count == 0)
        {
            return null;
        }

        var affectedTiles = 0;
        var maxTransitionCount = 0;
        var affectedRegions = new List<EffectRegion>();

        for (var index = 0; index < summary.Tiles.Count; index++)
        {
            var tile = summary.Tiles[index];
            var currentLevel = Classify(tile);
            var state = StateFor(index, tile.AverageLuminance, currentLevel);

            PruneTransitions(state, frame.Metadata.CapturedAt);
            if (IsTransition(state, currentLevel, tile.AverageLuminance))
            {
                state.Transitions.Enqueue(frame.Metadata.CapturedAt);
            }

            state.PreviousLevel = currentLevel;
            state.PreviousAverage = tile.AverageLuminance;

            if (state.Transitions.Count >= Options.MinimumTransitions)
            {
                affectedTiles++;
                maxTransitionCount = Math.Max(maxTransitionCount, state.Transitions.Count);
                affectedRegions.Add(TileRegion(tile, summary));
            }
        }

        if (affectedTiles == 0)
        {
            return null;
        }

        var affectedRatio = affectedTiles / (double)summary.Tiles.Count;
        var cooldownElapsed = frame.Metadata.CapturedAt - _lastActivation >= Options.Cooldown;
        if (!cooldownElapsed || affectedRatio < Options.MinimumAffectedTileRatio)
        {
            return null;
        }

        _lastActivation = frame.Metadata.CapturedAt;
        var transitionSeverity = Math.Clamp((maxTransitionCount - Options.MinimumTransitions + 1) / 4.0, 0, 1);
        var areaSeverity = Math.Clamp(affectedRatio / 0.35, 0, 1);
        var opacity = Math.Clamp(0.30 + (transitionSeverity * 0.18) + (areaSeverity * 0.18), 0, Options.MaximumOpacity);

        return new FlashEvent(
            frame.Metadata.CapturedAt,
            affectedTiles,
            summary.Tiles.Count,
            affectedRatio,
            maxTransitionCount,
            opacity,
            affectedRegions);
    }

    private static EffectRegion TileRegion(TileLuminanceStats tile, FrameTileSummary summary)
    {
        var width = 1.0 / summary.Columns;
        var height = 1.0 / summary.Rows;
        return new EffectRegion(tile.Column * width, tile.Row * height, width, height);
    }

    private TileFlashState StateFor(int index, double average, TileFlashLevel level)
    {
        if (_tiles.TryGetValue(index, out var state))
        {
            return state;
        }

        state = new TileFlashState(level, average);
        _tiles[index] = state;
        return state;
    }

    private TileFlashLevel Classify(TileLuminanceStats tile)
    {
        if (tile.AverageLuminance >= Options.BrightTileAverageThreshold ||
            tile.BrightPixelRatio >= Options.BrightTilePixelRatioThreshold)
        {
            return TileFlashLevel.Bright;
        }

        if (tile.AverageLuminance <= Options.DarkTileAverageThreshold)
        {
            return TileFlashLevel.Dark;
        }

        return TileFlashLevel.Neutral;
    }

    private bool IsTransition(TileFlashState state, TileFlashLevel currentLevel, double currentAverage)
    {
        if (currentLevel is TileFlashLevel.Neutral || state.PreviousLevel is TileFlashLevel.Neutral)
        {
            return false;
        }

        return currentLevel != state.PreviousLevel &&
            Math.Abs(currentAverage - state.PreviousAverage) >= Options.MinimumTransitionDelta;
    }

    private void PruneTransitions(TileFlashState state, DateTimeOffset now)
    {
        while (state.Transitions.Count > 0 && now - state.Transitions.Peek() > Options.Window)
        {
            state.Transitions.Dequeue();
        }
    }

    private enum TileFlashLevel
    {
        Neutral,
        Dark,
        Bright
    }

    private sealed class TileFlashState
    {
        public TileFlashState(TileFlashLevel previousLevel, double previousAverage)
        {
            PreviousLevel = previousLevel;
            PreviousAverage = previousAverage;
        }

        public TileFlashLevel PreviousLevel { get; set; }

        public double PreviousAverage { get; set; }

        public Queue<DateTimeOffset> Transitions { get; } = new();
    }
}
