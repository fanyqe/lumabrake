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
        var redDominantTiles = 0;
        var maxTransitionCount = 0;
        var affectedRegions = new List<EffectRegion>();

        for (var index = 0; index < summary.Tiles.Count; index++)
        {
            var tile = summary.Tiles[index];
            var current = Classify(tile);
            var state = StateFor(index, current);

            PruneTransitions(state, frame.Metadata.CapturedAt);
            if (IsTransition(state, current))
            {
                state.Transitions.Enqueue(new TileFlashTransition(
                    frame.Metadata.CapturedAt,
                    current.IsRedDominant || state.PreviousRedDominant));
            }

            state.PreviousLevel = current.Level;
            state.PreviousSignal = current.Signal;
            state.PreviousRedDominant = current.IsRedDominant;

            if (state.Transitions.Count >= Options.MinimumTransitions)
            {
                affectedTiles++;
                if (state.Transitions.Any(transition => transition.IsRedDominant))
                {
                    redDominantTiles++;
                }

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
            affectedRegions,
            redDominantTiles);
    }

    private static EffectRegion TileRegion(TileLuminanceStats tile, FrameTileSummary summary)
    {
        var width = 1.0 / summary.Columns;
        var height = 1.0 / summary.Rows;
        return new EffectRegion(tile.Column * width, tile.Row * height, width, height);
    }

    private TileFlashState StateFor(int index, TileFlashClassification classification)
    {
        if (_tiles.TryGetValue(index, out var state))
        {
            return state;
        }

        state = new TileFlashState(classification);
        _tiles[index] = state;
        return state;
    }

    private TileFlashClassification Classify(TileLuminanceStats tile)
    {
        if (Options.RedFlashDetectionEnabled &&
            tile.AverageRedDominance >= Options.RedDominanceThreshold &&
            tile.RedDominantPixelRatio >= Options.RedDominantPixelRatioThreshold)
        {
            return new TileFlashClassification(
                TileFlashLevel.Bright,
                Math.Max(tile.AverageRedDominance, tile.AverageLuminance),
                IsRedDominant: true);
        }

        if (tile.AverageLuminance >= Options.BrightTileAverageThreshold ||
            tile.BrightPixelRatio >= Options.BrightTilePixelRatioThreshold)
        {
            return new TileFlashClassification(TileFlashLevel.Bright, tile.AverageLuminance, IsRedDominant: false);
        }

        if (tile.AverageLuminance <= Options.DarkTileAverageThreshold)
        {
            return new TileFlashClassification(TileFlashLevel.Dark, tile.AverageLuminance, IsRedDominant: false);
        }

        return new TileFlashClassification(TileFlashLevel.Neutral, tile.AverageLuminance, IsRedDominant: false);
    }

    private bool IsTransition(TileFlashState state, TileFlashClassification current)
    {
        if (current.Level is TileFlashLevel.Neutral || state.PreviousLevel is TileFlashLevel.Neutral)
        {
            return false;
        }

        return current.Level != state.PreviousLevel &&
            Math.Abs(current.Signal - state.PreviousSignal) >= Options.MinimumTransitionDelta;
    }

    private void PruneTransitions(TileFlashState state, DateTimeOffset now)
    {
        while (state.Transitions.Count > 0 && now - state.Transitions.Peek().Timestamp > Options.Window)
        {
            state.Transitions.Dequeue();
        }
    }

    private readonly record struct TileFlashClassification(
        TileFlashLevel Level,
        double Signal,
        bool IsRedDominant);

    private readonly record struct TileFlashTransition(
        DateTimeOffset Timestamp,
        bool IsRedDominant);

    private enum TileFlashLevel
    {
        Neutral,
        Dark,
        Bright
    }

    private sealed class TileFlashState
    {
        public TileFlashState(TileFlashClassification previous)
        {
            PreviousLevel = previous.Level;
            PreviousSignal = previous.Signal;
            PreviousRedDominant = previous.IsRedDominant;
        }

        public TileFlashLevel PreviousLevel { get; set; }

        public double PreviousSignal { get; set; }

        public bool PreviousRedDominant { get; set; }

        public Queue<TileFlashTransition> Transitions { get; } = new();
    }
}
