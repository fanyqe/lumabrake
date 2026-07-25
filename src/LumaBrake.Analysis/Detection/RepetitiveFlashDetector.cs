using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Detection;

public sealed class RepetitiveFlashDetector
{
    private const double RegionalFlashMaximumAffectedTileRatio = 0.35;
    private const double ScrollLikeBrightSceneMinimumAffectedRatio = 0.01;
    private const double BroadBrightScenePercentile95Threshold = 0.62;
    private const double BroadBrightSceneBrightPixelRatioThreshold = 0.28;
    private const double DarkSceneAverageThreshold = 0.23;
    private const double DarkScenePercentile95Threshold = 0.42;
    private const double DarkSceneBrightPixelRatioThreshold = 0.12;
    private const double MotionCarryMaximumNeighborhoodSignatureDistance = 0.140;
    private const double MotionCarryMinimumNeighborhoodCoverage = 0.40;
    private const double MinimumShiftedRegionRetainedAreaRatio = 0.35;
    private static readonly TimeSpan PendingRegionalActivationMaximumAge = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan RecentDarkSceneWindow = TimeSpan.FromMilliseconds(1600);
    private readonly Dictionary<int, TileFlashState> _tiles = new();
    private DateTimeOffset _lastActivation = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastDarkSceneAt;
    private bool _previousFrameWasBroadBrightScene;
    private FrameTileSummary? _previousSummary;
    private FrameMetadata? _previousMetadata;
    private PendingRegionalFlashActivation? _pendingRegionalActivation;

    public RepetitiveFlashDetector(FlashDetectorOptions options)
    {
        Options = options;
    }

    public FlashDetectorOptions Options { get; }

    public FlashEvent? Observe(FrameAnalysisResult frame)
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

        var hadRecentDarkScene = HasRecentDarkScene(frame.Metadata.CapturedAt);
        var previousFrameWasBroadBrightScene = _previousFrameWasBroadBrightScene;
        PrepareTrackedMotion(frame, summary);
        RememberScene(frame);
        RemoveStaleTileStates(summary.Tiles.Count);

        try
        {
            var confirmedTileIndexes = new List<int>();
            var earlyTileIndexes = new List<int>();
            var earlyTransitionThreshold = EarlyLocalizedTransitionThreshold();

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
                        current.IsRedDominant || state.PreviousRedDominant,
                        current.SignalRegion ?? state.LastSignalRegion));
                }

                if (current.SignalRegion is not null)
                {
                    state.LastSignalRegion = current.SignalRegion;
                }

                state.PreviousLevel = current.Level;
                state.PreviousSignal = current.Signal;
                state.PreviousRedDominant = current.IsRedDominant;

                if (state.Transitions.Count >= Options.MinimumTransitions)
                {
                    confirmedTileIndexes.Add(index);
                }
                else if (state.Transitions.Count >= earlyTransitionThreshold)
                {
                    earlyTileIndexes.Add(index);
                }
            }

            var isEarlyLocalizedActivation = confirmedTileIndexes.Count == 0;
            var affectedTileIndexes = isEarlyLocalizedActivation ? earlyTileIndexes : confirmedTileIndexes;
            if (affectedTileIndexes.Count == 0)
            {
                return null;
            }

            var affectedRatio = affectedTileIndexes.Count / (double)summary.Tiles.Count;
            if (isEarlyLocalizedActivation &&
                affectedRatio > Math.Clamp(Options.EarlyLocalizedMaximumAffectedTileRatio, 0, 1))
            {
                return null;
            }

            var cooldownElapsed = frame.Metadata.CapturedAt - _lastActivation >= Options.Cooldown;
            if (!cooldownElapsed || affectedRatio < Math.Clamp(Options.MinimumAffectedTileRatio, 0, 1))
            {
                return null;
            }

            var maxTransitionCount = affectedTileIndexes.Max(index => _tiles[index].Transitions.Count);
            var activationThreshold = isEarlyLocalizedActivation
                ? earlyTransitionThreshold
                : Math.Max(1, Options.MinimumTransitions);
            var redDominantTiles = affectedTileIndexes.Count(index =>
                _tiles[index].Transitions.Any(transition => transition.IsRedDominant));
            if (ShouldSuppressScrollLikeBrightSceneFlash(
                frame,
                affectedRatio,
                redDominantTiles,
                hadRecentDarkScene,
                previousFrameWasBroadBrightScene))
            {
                return null;
            }

            var affectedRegions = affectedRatio < RegionalFlashMaximumAffectedTileRatio
                ? AffectedRegions(summary, affectedTileIndexes)
                : Array.Empty<EffectRegion>();
            if (affectedRatio < RegionalFlashMaximumAffectedTileRatio && affectedRegions.Length == 0)
            {
                return null;
            }

            if (affectedRegions.Length > 0 &&
                !ConfirmRegionalActivation(frame.Metadata.CapturedAt, affectedRegions))
            {
                return null;
            }

            _lastActivation = frame.Metadata.CapturedAt;
            var transitionSeverity = Math.Clamp((maxTransitionCount - activationThreshold + 1) / 4.0, 0, 1);
            var areaSeverity = Math.Clamp(affectedRatio / 0.35, 0, 1);
            var opacity = Math.Clamp(0.30 + (transitionSeverity * 0.18) + (areaSeverity * 0.18), 0, Options.MaximumOpacity);

            return new FlashEvent(
                frame.Metadata.CapturedAt,
                affectedTileIndexes.Count,
                summary.Tiles.Count,
                affectedRatio,
                maxTransitionCount,
                opacity,
                affectedRegions,
                redDominantTiles);
        }
        finally
        {
            RememberFrame(frame, summary);
        }
    }

    private void PrepareTrackedMotion(FrameAnalysisResult frame, FrameTileSummary summary)
    {
        if (_previousSummary is null || _previousMetadata is null)
        {
            return;
        }

        if (_previousMetadata.Width != frame.Metadata.Width ||
            _previousMetadata.Height != frame.Metadata.Height ||
            _previousSummary.Columns != summary.Columns ||
            _previousSummary.Rows != summary.Rows)
        {
            ResetTrackedTileState();
            return;
        }

        var frameGap = frame.Metadata.CapturedAt - _previousMetadata.CapturedAt;
        if (frameGap < TimeSpan.Zero || frameGap > MaximumTransitionInterval())
        {
            ResetTrackedTileState();
            return;
        }

        var motion = FrameTileMotionEstimator.Estimate(_previousSummary, summary);
        if (motion.HasMotion && CanCompensateMotion(motion))
        {
            ShiftTrackedTileState(_previousSummary, summary, motion);
            return;
        }

        if (motion.IsSceneChange)
        {
            _pendingRegionalActivation = null;
        }
    }

    private void ShiftTrackedTileState(
        FrameTileSummary previousSummary,
        FrameTileSummary summary,
        FrameTileMotion motion)
    {
        var motionCarriedStateCount = 0;
        var fixedCarriedStateCount = 0;
        var shiftedTiles = new Dictionary<int, TileFlashState>();
        foreach (var entry in _tiles)
        {
            var shiftedIndex = ShiftTileIndex(entry.Key, summary, motion.ColumnDelta, motion.RowDelta);
            if (shiftedIndex is not null &&
                CanCarryFlashState(previousSummary, summary, entry.Key, shiftedIndex.Value))
            {
                ShiftStateRegions(entry.Value, summary, motion.ColumnDelta, motion.RowDelta);
                shiftedTiles[shiftedIndex.Value] = entry.Value;
                motionCarriedStateCount++;
                continue;
            }

            if (entry.Key < summary.Tiles.Count &&
                CanCarryFlashState(previousSummary, summary, entry.Key, entry.Key))
            {
                ShiftStateRegions(entry.Value, summary, columnDelta: 0, rowDelta: 0);
                shiftedTiles[entry.Key] = entry.Value;
                fixedCarriedStateCount++;
            }
        }

        _tiles.Clear();
        foreach (var entry in shiftedTiles)
        {
            _tiles[entry.Key] = entry.Value;
        }

        if (motionCarriedStateCount > 0)
        {
            ShiftPendingRegionalActivation(summary, motion);
        }
        else if (fixedCarriedStateCount == 0)
        {
            _pendingRegionalActivation = null;
        }
    }

    private static bool CanCarryFlashState(
        FrameTileSummary previousSummary,
        FrameTileSummary currentSummary,
        int previousIndex,
        int currentIndex)
    {
        var neighborhood = NeighborhoodSignatureDistance(previousSummary, currentSummary, previousIndex, currentIndex);
        return neighborhood is null ||
            (neighborhood.Value.Coverage >= MotionCarryMinimumNeighborhoodCoverage &&
            neighborhood.Value.Distance <= MotionCarryMaximumNeighborhoodSignatureDistance);
    }

    private static int? ShiftTileIndex(int index, FrameTileSummary summary, int columnDelta, int rowDelta)
    {
        var currentColumn = (index % summary.Columns) + columnDelta;
        var currentRow = (index / summary.Columns) + rowDelta;
        if (currentColumn < 0 || currentColumn >= summary.Columns || currentRow < 0 || currentRow >= summary.Rows)
        {
            return null;
        }

        return (currentRow * summary.Columns) + currentColumn;
    }

    private static void ShiftStateRegions(
        TileFlashState state,
        FrameTileSummary summary,
        int columnDelta,
        int rowDelta)
    {
        state.LastSignalRegion = ShiftRegion(
            state.LastSignalRegion,
            columnDelta,
            rowDelta,
            summary,
            minimumRetainedAreaRatio: MinimumShiftedRegionRetainedAreaRatio);
        if (state.Transitions.Count == 0)
        {
            return;
        }

        var shiftedTransitions = state.Transitions
            .Select(transition => transition with
            {
                SignalRegion = ShiftRegion(
                    transition.SignalRegion,
                    columnDelta,
                    rowDelta,
                    summary,
                    minimumRetainedAreaRatio: MinimumShiftedRegionRetainedAreaRatio)
            })
            .ToArray();
        state.Transitions.Clear();
        foreach (var transition in shiftedTransitions)
        {
            state.Transitions.Enqueue(transition);
        }
    }

    private void ShiftPendingRegionalActivation(FrameTileSummary summary, FrameTileMotion motion)
    {
        if (_pendingRegionalActivation is null)
        {
            return;
        }

        var shiftedRegions = _pendingRegionalActivation.Regions
            .Select(region => ShiftRegion(
                region,
                motion.ColumnDelta,
                motion.RowDelta,
                summary,
                minimumRetainedAreaRatio: MinimumShiftedRegionRetainedAreaRatio))
            .Where(region => region is not null && region.Area > 0.0001)
            .Cast<EffectRegion>()
            .ToArray();
        _pendingRegionalActivation = shiftedRegions.Length == 0
            ? null
            : _pendingRegionalActivation with { Regions = shiftedRegions };
    }

    private static EffectRegion? ShiftRegion(
        EffectRegion? region,
        int columnDelta,
        int rowDelta,
        FrameTileSummary summary,
        double minimumRetainedAreaRatio = 0)
    {
        if (region is null)
        {
            return null;
        }

        var original = region.Clamp();
        var shifted = new EffectRegion(
            region.X + (columnDelta / (double)summary.Columns),
            region.Y + (rowDelta / (double)summary.Rows),
            region.Width,
            region.Height).Clamp();
        if (shifted.Area <= 0.0001)
        {
            return null;
        }

        if (minimumRetainedAreaRatio > 0 &&
            original.Area > 0 &&
            shifted.Area / original.Area < minimumRetainedAreaRatio)
        {
            return null;
        }

        return shifted;
    }

    private static MotionNeighborhoodMatch? NeighborhoodSignatureDistance(
        FrameTileSummary previousSummary,
        FrameTileSummary currentSummary,
        int previousIndex,
        int currentIndex)
    {
        var previousColumn = previousIndex % previousSummary.Columns;
        var previousRow = previousIndex / previousSummary.Columns;
        var currentColumn = currentIndex % currentSummary.Columns;
        var currentRow = currentIndex / currentSummary.Columns;
        var distance = 0.0;
        var compared = 0;
        var possible = 0;

        for (var rowOffset = -1; rowOffset <= 1; rowOffset++)
        {
            for (var columnOffset = -1; columnOffset <= 1; columnOffset++)
            {
                if (rowOffset == 0 && columnOffset == 0)
                {
                    continue;
                }

                possible++;
                var neighborPreviousColumn = previousColumn + columnOffset;
                var neighborPreviousRow = previousRow + rowOffset;
                var neighborCurrentColumn = currentColumn + columnOffset;
                var neighborCurrentRow = currentRow + rowOffset;
                if (neighborPreviousColumn < 0 ||
                    neighborPreviousColumn >= previousSummary.Columns ||
                    neighborPreviousRow < 0 ||
                    neighborPreviousRow >= previousSummary.Rows ||
                    neighborCurrentColumn < 0 ||
                    neighborCurrentColumn >= currentSummary.Columns ||
                    neighborCurrentRow < 0 ||
                    neighborCurrentRow >= currentSummary.Rows)
                {
                    continue;
                }

                var previousTile = previousSummary.Tiles[(neighborPreviousRow * previousSummary.Columns) + neighborPreviousColumn];
                var currentTile = currentSummary.Tiles[(neighborCurrentRow * currentSummary.Columns) + neighborCurrentColumn];
                distance += TileSignatureDistance(previousTile, currentTile);
                compared++;
            }
        }

        return compared == 0 || possible == 0
            ? null
            : new MotionNeighborhoodMatch(distance / compared, compared / (double)possible);
    }

    private static double TileSignatureDistance(TileLuminanceStats previous, TileLuminanceStats current) =>
        (Math.Abs(previous.AverageLuminance - current.AverageLuminance) * 0.52) +
        (Math.Abs(previous.Percentile95Luminance - current.Percentile95Luminance) * 0.30) +
        (Math.Abs(previous.BrightPixelRatio - current.BrightPixelRatio) * 0.14) +
        (Math.Abs(previous.AverageRedDominance - current.AverageRedDominance) * 0.02) +
        (Math.Abs(previous.RedDominantPixelRatio - current.RedDominantPixelRatio) * 0.02);

    private void ResetTrackedTileState()
    {
        _tiles.Clear();
        _pendingRegionalActivation = null;
    }

    private static bool CanCompensateMotion(FrameTileMotion motion) =>
        motion.IsReliable || motion.IsMotionLikely;

    private EffectRegion[] AffectedRegions(FrameTileSummary summary, IReadOnlyList<int> tileIndexes) =>
        RegionalMaskBuilder.Build(summary, tileIndexes, new RegionalMaskBuildOptions
        {
            Padding = Options.RegionalSignalPadding,
            MergeTolerance = Options.RegionalSignalPadding,
            MaskFit = Options.RegionalMaskFit,
            IncludeNeighboringBrightTiles = true,
            MaximumTileRatio = RegionalFlashMaximumAffectedTileRatio,
            NeighborBrightAverageThreshold = Options.BrightTileAverageThreshold,
            NeighborBrightPercentile95Threshold = Options.BrightTileAverageThreshold,
            NeighborBrightPixelRatioThreshold = Options.SignalTilePixelRatioThreshold,
            NeighborRedDominantPixelRatioThreshold = Options.RedDominantPixelRatioThreshold,
            MinimumSingleTileAverageLuminance = Options.BrightTileAverageThreshold,
            MinimumSingleTilePercentile95Luminance = Options.BrightTileAverageThreshold,
            MinimumSingleTileBrightPixelRatio = Options.SignalTilePixelRatioThreshold,
            MinimumSingleTileRedPixelRatio = Options.RedDominantPixelRatioThreshold,
            AllowSingleTileWithoutSignal = true,
            TrustPreferredSignalRegion = true,
            PreferredSignalRegion = (index, _) =>
            {
                var state = _tiles[index];
                return LastTransitionSignalRegion(state) ?? state.LastSignalRegion;
            }
        }).Regions.ToArray();

    private bool ConfirmRegionalActivation(DateTimeOffset timestamp, IReadOnlyList<EffectRegion> regions)
    {
        if (_pendingRegionalActivation is not null &&
            timestamp - _pendingRegionalActivation.Timestamp <= PendingRegionalActivationMaximumAge &&
            RegionsOverlap(_pendingRegionalActivation.Regions, regions))
        {
            _pendingRegionalActivation = null;
            return true;
        }

        _pendingRegionalActivation = new PendingRegionalFlashActivation(timestamp, regions.ToArray());
        return false;
    }

    private static bool RegionsOverlap(IReadOnlyList<EffectRegion> first, IReadOnlyList<EffectRegion> second) =>
        first.Any(firstRegion => second.Any(secondRegion => firstRegion.TouchesOrOverlaps(secondRegion, 0.025)));

    private bool ShouldSuppressScrollLikeBrightSceneFlash(
        FrameAnalysisResult frame,
        double affectedRatio,
        int redDominantTiles,
        bool hadRecentDarkScene,
        bool previousFrameWasBroadBrightScene) =>
        redDominantTiles == 0 &&
        Options.SuppressLocalizedBrightSceneScroll &&
        !hadRecentDarkScene &&
        previousFrameWasBroadBrightScene &&
        affectedRatio >= ScrollLikeBrightSceneMinimumAffectedRatio &&
        IsBroadBrightScene(frame);

    private bool HasRecentDarkScene(DateTimeOffset now) =>
        _lastDarkSceneAt is not null && now - _lastDarkSceneAt.Value <= RecentDarkSceneWindow;

    private void RememberScene(FrameAnalysisResult frame)
    {
        if (frame.AverageLuminance <= DarkSceneAverageThreshold &&
            frame.Percentile95Luminance <= DarkScenePercentile95Threshold &&
            frame.BrightPixelRatio <= DarkSceneBrightPixelRatioThreshold)
        {
            _lastDarkSceneAt = frame.Metadata.CapturedAt;
        }

        _previousFrameWasBroadBrightScene = IsBroadBrightScene(frame);
    }

    private void RememberFrame(FrameAnalysisResult frame, FrameTileSummary summary)
    {
        _previousSummary = summary;
        _previousMetadata = frame.Metadata;
    }

    private static bool IsBroadBrightScene(FrameAnalysisResult frame) =>
        frame.BrightPixelRatio >= BroadBrightSceneBrightPixelRatioThreshold &&
        frame.Percentile95Luminance >= BroadBrightScenePercentile95Threshold;

    private static EffectRegion? LastTransitionSignalRegion(TileFlashState state)
    {
        foreach (var transition in state.Transitions.Reverse())
        {
            if (transition.SignalRegion is not null)
            {
                return transition.SignalRegion;
            }
        }

        return null;
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
        var signalRegion = tile.SignalBounds;
        if (Options.RedFlashDetectionEnabled &&
            tile.AverageRedDominance >= Options.RedDominanceThreshold &&
            tile.RedDominantPixelRatio >= Options.RedDominantPixelRatioThreshold)
        {
            return new TileFlashClassification(
                TileFlashLevel.Bright,
                Math.Max(tile.AverageRedDominance, tile.AverageLuminance),
                IsRedDominant: true,
                signalRegion);
        }

        if (tile.AverageLuminance >= Options.BrightTileAverageThreshold ||
            tile.BrightPixelRatio >= Options.BrightTilePixelRatioThreshold ||
            (signalRegion is not null && tile.BrightPixelRatio >= Options.SignalTilePixelRatioThreshold))
        {
            return new TileFlashClassification(
                TileFlashLevel.Bright,
                Math.Max(tile.AverageLuminance, tile.BrightPixelRatio),
                IsRedDominant: false,
                signalRegion);
        }

        if (tile.AverageLuminance <= Options.DarkTileAverageThreshold)
        {
            return new TileFlashClassification(
                TileFlashLevel.Dark,
                tile.AverageLuminance,
                IsRedDominant: false,
                SignalRegion: null);
        }

        return new TileFlashClassification(
            TileFlashLevel.Neutral,
            tile.AverageLuminance,
            IsRedDominant: false,
            signalRegion);
    }

    private bool IsTransition(TileFlashState state, TileFlashClassification current)
    {
        var delta = Math.Abs(current.Signal - state.PreviousSignal);
        if (delta < Options.MinimumTransitionDelta)
        {
            return false;
        }

        if (current.Level == state.PreviousLevel)
        {
            return current.IsRedDominant != state.PreviousRedDominant &&
                (current.IsRedDominant || state.PreviousRedDominant);
        }

        return current.Level is TileFlashLevel.Bright ||
            state.PreviousLevel is TileFlashLevel.Bright ||
            (current.Level is not TileFlashLevel.Neutral && state.PreviousLevel is not TileFlashLevel.Neutral);
    }

    private void PruneTransitions(TileFlashState state, DateTimeOffset now)
    {
        if (state.Transitions.Count > 0)
        {
            var transitionGap = now - state.Transitions.Last().Timestamp;
            if (transitionGap < TimeSpan.Zero || transitionGap > MaximumTransitionInterval())
            {
                state.Transitions.Clear();
                _pendingRegionalActivation = null;
                return;
            }
        }

        while (state.Transitions.Count > 0 && now - state.Transitions.Peek().Timestamp > Options.Window)
        {
            state.Transitions.Dequeue();
        }
    }

    private TimeSpan MaximumTransitionInterval()
    {
        if (Options.MaximumTransitionInterval <= TimeSpan.Zero)
        {
            return Options.Window;
        }

        return Options.MaximumTransitionInterval > Options.Window
            ? Options.Window
            : Options.MaximumTransitionInterval;
    }

    private void RemoveStaleTileStates(int currentTileCount)
    {
        foreach (var removedIndex in _tiles.Keys.Where(index => index >= currentTileCount).ToArray())
        {
            _tiles.Remove(removedIndex);
        }
    }

    private int EarlyLocalizedTransitionThreshold() =>
        Math.Clamp(Options.EarlyLocalizedTransitions, 1, Math.Max(1, Options.MinimumTransitions));

    private readonly record struct TileFlashClassification(
        TileFlashLevel Level,
        double Signal,
        bool IsRedDominant,
        EffectRegion? SignalRegion);

    private readonly record struct TileFlashTransition(
        DateTimeOffset Timestamp,
        bool IsRedDominant,
        EffectRegion? SignalRegion);

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

        public EffectRegion? LastSignalRegion { get; set; }

        public Queue<TileFlashTransition> Transitions { get; } = new();
    }

    private sealed record PendingRegionalFlashActivation(
        DateTimeOffset Timestamp,
        IReadOnlyList<EffectRegion> Regions);

    private readonly record struct MotionNeighborhoodMatch(double Distance, double Coverage);
}
