using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Detection;

public sealed class StaticShadeDetector
{
    private const double RegionalActivationOverlapTolerance = 0.025;
    private const double SignalMovementTolerance = 0.02;
    private const double MotionCarryMaximumTileSignatureDistance = 0.045;
    private const double MotionCarryMaximumNeighborhoodSignatureDistance = 0.060;
    private const double MotionCarryMinimumNeighborhoodCoverage = 0.45;
    private const double MinimumShiftedRegionRetainedAreaRatio = 0.35;
    private static readonly TimeSpan PendingRegionalActivationMaximumAge = TimeSpan.FromMilliseconds(1400);
    private static readonly TimeSpan ConfirmedRegionalActivationMaximumAge = TimeSpan.FromMilliseconds(2200);
    private readonly Dictionary<int, TileStaticState> _tiles = new();
    private RegionalActivation? _pendingRegionalActivation;
    private RegionalActivation? _confirmedRegionalActivation;
    private DateTimeOffset? _lastActivation;
    private FrameTileSummary? _previousSummary;
    private FrameMetadata? _previousMetadata;

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

        var shouldClearRegionalEffect = PrepareTrackedMotion(frame, summary);
        RemoveStaleTileStates(summary.Tiles.Count);

        var candidates = new List<StaticTileCandidate>();

        for (var index = 0; index < summary.Tiles.Count; index++)
        {
            var tile = summary.Tiles[index];
            var state = StateFor(index, tile, frame.Metadata.CapturedAt);
            var signalSurface = IsBrightSignalSurface(summary, tile);
            var isBright = IsBright(tile, signalSurface);
            var frameGap = frame.Metadata.CapturedAt - state.LastSeen;
            if (frameGap < TimeSpan.Zero || frameGap > Options.MaximumFrameGap)
            {
                state.StaticSince = isBright ? frame.Metadata.CapturedAt : null;
                state.Remember(tile);
                state.LastSeen = frame.Metadata.CapturedAt;
                continue;
            }

            var averageDelta = Math.Abs(tile.AverageLuminance - state.PreviousAverage);
            var signalStable = SignalStable(state.PreviousSignalBounds, tile.SignalBounds);
            var stable = signalStable &&
                (averageDelta <= Options.MaximumAverageDelta || SignalSurfaceStable(tile, state, signalSurface));

            if (isBright && stable)
            {
                state.StaticSince ??= state.LastSeen;
                var duration = frame.Metadata.CapturedAt - state.StaticSince.Value;
                var exposureScore = ExposureScore(tile, duration, signalSurface);
                if (exposureScore >= Options.MinimumExposureScore)
                {
                    var normalReady = duration >= Options.MinimumStaticDuration;
                    var quickReady = !normalReady &&
                        CanUseReacquireTiming(frame.Metadata.CapturedAt) &&
                        Options.ReacquireMinimumStaticDuration > TimeSpan.Zero &&
                        duration >= Options.ReacquireMinimumStaticDuration;
                    if (normalReady || quickReady)
                    {
                        candidates.Add(new StaticTileCandidate(index, duration, exposureScore, QuickReacquireOnly: quickReady));
                    }
                }
            }
            else
            {
                state.StaticSince = isBright ? frame.Metadata.CapturedAt : null;
            }

            state.Remember(tile);
            state.LastSeen = frame.Metadata.CapturedAt;
        }

        var selectedCandidates = SelectCandidatesForActivation(summary, candidates, frame.Metadata.CapturedAt);
        if (selectedCandidates.Count == 0)
        {
            var result = shouldClearRegionalEffect || ClearObsoleteRegionalEffect(summary)
                ? ClearRegionalEffect(frame.Metadata.CapturedAt)
                : null;
            RememberFrame(frame, summary);
            return result;
        }

        var affectedMasks = BuildAffectedMasks(summary, selectedCandidates.Select(candidate => candidate.TileIndex).ToArray());
        if (affectedMasks.Regions.Count == 0)
        {
            var result = shouldClearRegionalEffect ? ClearRegionalEffect(frame.Metadata.CapturedAt) : null;
            RememberFrame(frame, summary);
            return result;
        }

        var affectedRatio = affectedMasks.TileCount / (double)summary.Tiles.Count;
        if (affectedRatio < Options.MinimumAffectedTileRatio ||
            affectedRatio > Options.MaximumAffectedTileRatio)
        {
            var result = shouldClearRegionalEffect ? ClearRegionalEffect(frame.Metadata.CapturedAt) : null;
            RememberFrame(frame, summary);
            return result;
        }

        if (!ConfirmRegionalActivation(frame.Metadata.CapturedAt, affectedMasks.Regions))
        {
            var result = shouldClearRegionalEffect ? ClearRegionalEffect(frame.Metadata.CapturedAt) : null;
            RememberFrame(frame, summary);
            return result;
        }

        _lastActivation = frame.Metadata.CapturedAt;

        var longestStaticDuration = selectedCandidates.Max(candidate => candidate.StaticDuration);
        var exposureSeverity = selectedCandidates.Average(candidate => candidate.ExposureScore);
        var severity = Math.Max(StaticSeverity(longestStaticDuration), exposureSeverity);
        var opacity = Math.Clamp(
            Options.BaseOpacity + (severity * (Options.MaximumOpacity - Options.BaseOpacity)),
            0,
            Options.MaximumOpacity);

        var staticShade = new StaticShadeEvent(
            frame.Metadata.CapturedAt,
            affectedMasks.TileCount,
            summary.Tiles.Count,
            affectedRatio,
            longestStaticDuration,
            opacity,
            affectedMasks.Regions,
            exposureSeverity);
        RememberFrame(frame, summary);
        return staticShade;
    }

    private void RemoveStaleTileStates(int currentTileCount)
    {
        foreach (var removedIndex in _tiles.Keys.Where(index => index >= currentTileCount).ToArray())
        {
            _tiles.Remove(removedIndex);
        }
    }

    private bool PrepareTrackedMotion(FrameAnalysisResult frame, FrameTileSummary summary)
    {
        if (_previousSummary is null || _previousMetadata is null)
        {
            return false;
        }

        if (_previousMetadata.Width != frame.Metadata.Width ||
            _previousMetadata.Height != frame.Metadata.Height ||
            _previousSummary.Columns != summary.Columns ||
            _previousSummary.Rows != summary.Rows)
        {
            return ResetTrackedRegionalState();
        }

        var frameGap = frame.Metadata.CapturedAt - _previousMetadata.CapturedAt;
        if (frameGap < TimeSpan.Zero || frameGap > Options.MaximumFrameGap)
        {
            return ResetTrackedRegionalState();
        }

        var motion = FrameTileMotionEstimator.Estimate(_previousSummary, summary);
        if (motion.IsSceneChange)
        {
            return ResetTrackedRegionalState();
        }

        return motion.HasMotion && CanCompensateMotion(motion) && ShiftTrackedRegionalState(_previousSummary, summary, motion);
    }

    private static bool CanCompensateMotion(FrameTileMotion motion) =>
        motion.IsReliable || motion.IsMotionLikely;

    private bool ShiftTrackedRegionalState(
        FrameTileSummary previousSummary,
        FrameTileSummary summary,
        FrameTileMotion motion)
    {
        var shouldClearRegionalEffect = false;
        var motionCarriedStateCount = 0;
        var fixedCarriedStateCount = 0;
        var shiftedTiles = new Dictionary<int, TileStaticState>();
        foreach (var entry in _tiles)
        {
            var shiftedIndex = ShiftTileIndex(entry.Key, summary, motion.ColumnDelta, motion.RowDelta);
            if (shiftedIndex is not null &&
                TryCarryStaticState(previousSummary, summary, entry.Key, shiftedIndex.Value, entry.Value, out var shiftedSignalBounds))
            {
                entry.Value.PreviousSignalBounds = shiftedSignalBounds;
                shiftedTiles[shiftedIndex.Value] = entry.Value;
                motionCarriedStateCount++;
                continue;
            }

            if (entry.Key < summary.Tiles.Count &&
                TryCarryStaticState(previousSummary, summary, entry.Key, entry.Key, entry.Value, out var fixedSignalBounds))
            {
                entry.Value.PreviousSignalBounds = fixedSignalBounds;
                shiftedTiles[entry.Key] = entry.Value;
                fixedCarriedStateCount++;
                continue;
            }

            shouldClearRegionalEffect = true;
        }

        _tiles.Clear();
        foreach (var entry in shiftedTiles)
        {
            _tiles[entry.Key] = entry.Value;
        }

        if (motionCarriedStateCount > 0 || fixedCarriedStateCount == 0)
        {
            shouldClearRegionalEffect |= ShiftActivation(ref _pendingRegionalActivation, summary, motion);
            shouldClearRegionalEffect |= ShiftActivation(ref _confirmedRegionalActivation, summary, motion);
        }

        return shouldClearRegionalEffect;
    }

    private bool TryCarryStaticState(
        FrameTileSummary previousSummary,
        FrameTileSummary currentSummary,
        int previousIndex,
        int currentIndex,
        TileStaticState state,
        out EffectRegion? carriedSignalBounds)
    {
        carriedSignalBounds = null;
        var previousTile = previousSummary.Tiles[previousIndex];
        var currentTile = currentSummary.Tiles[currentIndex];
        if (TileSignatureDistance(previousTile, currentTile) > MotionCarryMaximumTileSignatureDistance)
        {
            return false;
        }

        var neighborhood = NeighborhoodSignatureDistance(previousSummary, currentSummary, previousIndex, currentIndex);
        var neighborhoodMatches = neighborhood is null ||
            (neighborhood.Value.Coverage >= MotionCarryMinimumNeighborhoodCoverage &&
            neighborhood.Value.Distance <= MotionCarryMaximumNeighborhoodSignatureDistance);
        var shiftedSignal = ShiftRegion(
            state.PreviousSignalBounds,
            currentTile.Column - previousTile.Column,
            currentTile.Row - previousTile.Row,
            currentSummary,
            minimumRetainedAreaRatio: MinimumShiftedRegionRetainedAreaRatio);
        if (state.PreviousSignalBounds is not null)
        {
            if (shiftedSignal is null ||
                currentTile.SignalBounds is null ||
                !SignalStable(shiftedSignal, currentTile.SignalBounds) ||
                !neighborhoodMatches)
            {
                return false;
            }

            carriedSignalBounds = shiftedSignal;
            return true;
        }

        carriedSignalBounds = currentTile.SignalBounds;
        return neighborhoodMatches;
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

    private static bool ShiftActivation(
        ref RegionalActivation? activation,
        FrameTileSummary summary,
        FrameTileMotion motion)
    {
        if (activation is null)
        {
            return false;
        }

        var shiftedRegions = activation.Regions
            .Select(region => ShiftRegion(
                region,
                motion.ColumnDelta,
                motion.RowDelta,
                summary,
                minimumRetainedAreaRatio: MinimumShiftedRegionRetainedAreaRatio))
            .Where(region => region is not null && region.Area > 0.0001)
            .Cast<EffectRegion>()
            .ToArray();
        if (shiftedRegions.Length == 0)
        {
            activation = null;
            return true;
        }

        activation = activation with { Regions = shiftedRegions };
        return false;
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

    private bool ResetTrackedRegionalState()
    {
        var hadTrackedRegionalState = HasTrackedRegionalState;
        _tiles.Clear();
        _pendingRegionalActivation = null;
        _confirmedRegionalActivation = null;
        _lastActivation = null;
        return hadTrackedRegionalState;
    }

    private bool ClearObsoleteRegionalEffect(FrameTileSummary summary)
    {
        if (_confirmedRegionalActivation is null)
        {
            return false;
        }

        if (_confirmedRegionalActivation.Regions.Any(region => HasBrightSignalNearRegion(summary, region)))
        {
            return false;
        }

        _pendingRegionalActivation = null;
        _confirmedRegionalActivation = null;
        _lastActivation = null;
        return true;
    }

    private bool HasBrightSignalNearRegion(FrameTileSummary summary, EffectRegion region)
    {
        foreach (var tile in summary.Tiles)
        {
            var signalSurface = IsBrightSignalSurface(summary, tile);
            if (!IsBright(tile, signalSurface))
            {
                continue;
            }

            var candidateRegion = tile.SignalBounds ?? RegionalMaskBuilder.TileRegion(tile, summary);
            if (candidateRegion.TouchesOrOverlaps(region, RegionalActivationOverlapTolerance))
            {
                return true;
            }
        }

        return false;
    }

    private void RememberFrame(FrameAnalysisResult frame, FrameTileSummary summary)
    {
        _previousSummary = summary;
        _previousMetadata = frame.Metadata;
    }

    private bool HasTrackedRegionalState =>
        _pendingRegionalActivation is not null ||
        _confirmedRegionalActivation is not null ||
        _lastActivation is not null;

    private static StaticShadeEvent ClearRegionalEffect(DateTimeOffset timestamp) =>
        new(
            timestamp,
            AffectedTileCount: 0,
            TotalTileCount: 0,
            AffectedTileRatio: 0,
            LongestStaticDuration: TimeSpan.Zero,
            RecommendedOpacity: 0,
            AffectedRegions: Array.Empty<EffectRegion>(),
            ExposureScore: 0);

    private IReadOnlyList<StaticTileCandidate> SelectCandidatesForActivation(
        FrameTileSummary summary,
        IReadOnlyList<StaticTileCandidate> candidates,
        DateTimeOffset timestamp)
    {
        var selected = candidates.Where(candidate => !candidate.QuickReacquireOnly).ToList();
        var quickCandidates = candidates.Where(candidate => candidate.QuickReacquireOnly).ToArray();
        if (quickCandidates.Length == 0)
        {
            return selected;
        }

        var quickMasks = BuildAffectedMasks(summary, quickCandidates.Select(candidate => candidate.TileIndex).ToArray());
        if (quickMasks.Regions.Count > 0 && CanFastReacquire(timestamp, quickMasks.Regions))
        {
            selected.AddRange(quickCandidates);
        }

        return selected;
    }

    private RegionalMaskBuildResult BuildAffectedMasks(FrameTileSummary summary, IReadOnlyCollection<int> affectedTileIndexes) =>
        RegionalMaskBuilder.Build(summary, affectedTileIndexes, new RegionalMaskBuildOptions
        {
            Padding = Options.RegionalSignalPadding,
            MergeTolerance = Options.RegionalSignalPadding,
            MaskFit = Options.RegionalMaskFit,
            IncludeNeighboringBrightTiles = true,
            MaximumTileRatio = Options.MaximumAffectedTileRatio,
            NeighborBrightAverageThreshold = Options.BrightTileAverageThreshold,
            NeighborBrightPercentile95Threshold = Options.BrightTilePercentile95Threshold,
            NeighborBrightPixelRatioThreshold = Options.BrightTilePixelRatioThreshold,
            MinimumSingleTileAverageLuminance = Options.BrightTileAverageThreshold,
            MinimumSingleTilePercentile95Luminance = Options.BrightTilePercentile95Threshold,
            MinimumSingleTileBrightPixelRatio = Options.BrightTilePixelRatioThreshold,
            MinimumComponentSignalAreaRatio = 0.025
        });

    private TileStaticState StateFor(int index, TileLuminanceStats tile, DateTimeOffset timestamp)
    {
        if (_tiles.TryGetValue(index, out var state))
        {
            return state;
        }

        state = new TileStaticState(tile, timestamp);
        _tiles[index] = state;
        return state;
    }

    private bool IsBright(TileLuminanceStats tile, bool signalSurface) =>
        tile.AverageLuminance >= Options.BrightTileAverageThreshold ||
        tile.Percentile95Luminance >= Options.BrightTilePercentile95Threshold ||
        tile.BrightPixelRatio >= Options.BrightTilePixelRatioThreshold ||
        signalSurface;

    private bool IsBrightSignalSurface(FrameTileSummary summary, TileLuminanceStats tile)
    {
        if (tile.SignalBounds is null || tile.Percentile95Luminance < Options.SignalSurfaceMinimumPercentile95Luminance)
        {
            return false;
        }

        var tileRegion = RegionalMaskBuilder.TileRegion(tile, summary);
        var coverage = tileRegion.Area <= 0 ? 0 : tile.SignalBounds.Clamp().Area / tileRegion.Area;
        return coverage >= Options.SignalSurfaceMinimumCoverageRatio;
    }

    private bool SignalSurfaceStable(TileLuminanceStats tile, TileStaticState state, bool signalSurface) =>
        signalSurface &&
        Math.Abs(tile.Percentile95Luminance - state.PreviousPercentile95) <= Options.MaximumSignalSurfacePercentile95Delta &&
        Math.Abs(tile.BrightPixelRatio - state.PreviousBrightPixelRatio) <= Options.MaximumSignalSurfaceBrightPixelRatioDelta;

    private double ExposureScore(TileLuminanceStats tile, TimeSpan duration, bool signalSurface)
    {
        var luminanceScore = Math.Max(
            NormalizedAboveThreshold(tile.AverageLuminance, Options.BrightTileAverageThreshold),
            NormalizedAboveThreshold(tile.Percentile95Luminance, Options.BrightTilePercentile95Threshold));
        var brightPixelScore = NormalizedAboveThreshold(tile.BrightPixelRatio, Options.BrightTilePixelRatioThreshold);
        var durationScore = StaticSeverity(duration);
        var signalSurfaceScore = signalSurface
            ? NormalizedAboveThreshold(tile.Percentile95Luminance, Options.SignalSurfaceMinimumPercentile95Luminance)
            : 0;

        var luminanceWeight = PositiveWeight(Options.ExposureLuminanceWeight);
        var brightPixelWeight = PositiveWeight(Options.ExposureBrightPixelWeight);
        var durationWeight = PositiveWeight(Options.ExposureDurationWeight);
        var signalSurfaceWeight = signalSurface ? PositiveWeight(Options.ExposureSignalSurfaceWeight) : 0;
        var weightSum = luminanceWeight + brightPixelWeight + durationWeight + signalSurfaceWeight;
        if (weightSum <= 0)
        {
            return Math.Max(luminanceScore, Math.Max(brightPixelScore, Math.Max(durationScore, signalSurfaceScore)));
        }

        return Math.Clamp(
            ((luminanceScore * luminanceWeight) +
            (brightPixelScore * brightPixelWeight) +
            (durationScore * durationWeight) +
            (signalSurfaceScore * signalSurfaceWeight)) /
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

    private bool CanUseReacquireTiming(DateTimeOffset timestamp) =>
        Options.ReacquireWindow > TimeSpan.Zero &&
        _lastActivation is not null &&
        timestamp - _lastActivation.Value >= TimeSpan.Zero &&
        timestamp - _lastActivation.Value >= Options.ReacquireMinimumStaticDuration &&
        timestamp - _lastActivation.Value <= Options.ReacquireWindow &&
        Options.ReacquireMinimumStaticDuration < Options.MinimumStaticDuration;

    private bool CanFastReacquire(DateTimeOffset timestamp, IReadOnlyList<EffectRegion> regions) =>
        _confirmedRegionalActivation is not null &&
        timestamp - _confirmedRegionalActivation.Timestamp >= TimeSpan.Zero &&
        timestamp - _confirmedRegionalActivation.Timestamp <= Options.ReacquireWindow &&
        RegionsOverlap(_confirmedRegionalActivation.Regions, regions);

    private bool ConfirmRegionalActivation(DateTimeOffset timestamp, IReadOnlyList<EffectRegion> regions)
    {
        if (_confirmedRegionalActivation is not null &&
            timestamp - _confirmedRegionalActivation.Timestamp <= ConfirmedRegionalActivationMaximumAge &&
            RegionsOverlap(_confirmedRegionalActivation.Regions, regions))
        {
            _confirmedRegionalActivation = new RegionalActivation(timestamp, regions.ToArray());
            _pendingRegionalActivation = null;
            return true;
        }

        if (_pendingRegionalActivation is not null &&
            timestamp - _pendingRegionalActivation.Timestamp <= PendingRegionalActivationMaximumAge &&
            RegionsOverlap(_pendingRegionalActivation.Regions, regions))
        {
            _confirmedRegionalActivation = new RegionalActivation(timestamp, regions.ToArray());
            _pendingRegionalActivation = null;
            return true;
        }

        _pendingRegionalActivation = new RegionalActivation(timestamp, regions.ToArray());
        return false;
    }

    private static bool RegionsOverlap(IReadOnlyList<EffectRegion> first, IReadOnlyList<EffectRegion> second) =>
        first.Any(firstRegion => second.Any(secondRegion =>
            firstRegion.TouchesOrOverlaps(secondRegion, RegionalActivationOverlapTolerance)));

    private static bool SignalStable(EffectRegion? previous, EffectRegion? current)
    {
        if (previous is null && current is null)
        {
            return true;
        }

        if (previous is null || current is null)
        {
            return false;
        }

        var first = previous.Clamp();
        var second = current.Clamp();
        var firstCenterX = first.X + (first.Width / 2);
        var firstCenterY = first.Y + (first.Height / 2);
        var secondCenterX = second.X + (second.Width / 2);
        var secondCenterY = second.Y + (second.Height / 2);

        return Math.Abs(firstCenterX - secondCenterX) <= SignalMovementTolerance &&
            Math.Abs(firstCenterY - secondCenterY) <= SignalMovementTolerance &&
            Math.Abs(first.Width - second.Width) <= SignalMovementTolerance &&
            Math.Abs(first.Height - second.Height) <= SignalMovementTolerance;
    }

    private sealed class TileStaticState
    {
        public TileStaticState(TileLuminanceStats tile, DateTimeOffset lastSeen)
        {
            Remember(tile);
            LastSeen = lastSeen;
        }

        public double PreviousAverage { get; set; }

        public DateTimeOffset LastSeen { get; set; }

        public DateTimeOffset? StaticSince { get; set; }

        public EffectRegion? PreviousSignalBounds { get; set; }

        public double PreviousPercentile95 { get; set; }

        public double PreviousBrightPixelRatio { get; set; }

        public void Remember(TileLuminanceStats tile)
        {
            PreviousAverage = tile.AverageLuminance;
            PreviousPercentile95 = tile.Percentile95Luminance;
            PreviousBrightPixelRatio = tile.BrightPixelRatio;
            PreviousSignalBounds = tile.SignalBounds;
        }
    }

    private sealed record RegionalActivation(DateTimeOffset Timestamp, IReadOnlyList<EffectRegion> Regions);

    private readonly record struct MotionNeighborhoodMatch(double Distance, double Coverage);

    private sealed record StaticTileCandidate(
        int TileIndex,
        TimeSpan StaticDuration,
        double ExposureScore,
        bool QuickReacquireOnly);
}
