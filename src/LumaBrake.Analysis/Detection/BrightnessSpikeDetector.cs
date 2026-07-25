using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Detection;

public sealed class BrightnessSpikeDetector
{
    private const double ImmediateLocalizedSpikeMinimumTileRatio = 0.02;
    private const double EstablishedBrightSceneBaselineThreshold = 0.26;
    private const double EstablishedBrightSceneAverageThreshold = 0.24;
    private const double EstablishedBrightScenePercentile95Threshold = 0.62;
    private const double EstablishedBrightSceneBrightPixelRatioThreshold = 0.22;
    private static readonly TimeSpan PreviousTileComparisonMaximumFrameGap = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan PendingLocalizedSpikeMaximumAge = TimeSpan.FromMilliseconds(220);
    private readonly Queue<double> _baseline = new();
    private FrameTileSummary? _previousSummary;
    private FrameMetadata? _previousMetadata;
    private DateTimeOffset _lastActivation = DateTimeOffset.MinValue;
    private LocalizedSpikeCandidate? _pendingLocalizedSpike;

    public BrightnessSpikeDetector(SpikeDetectorOptions options)
    {
        Options = options;
    }

    public SpikeDetectorOptions Options { get; }

    public SpikeEvent? Observe(FrameAnalysisResult frame)
    {
        if (!Options.Enabled)
        {
            return null;
        }

        if (ResetAfterUntrustedFrameGap(frame))
        {
            AddBaseline(frame.AverageLuminance);
            RememberFrame(frame);
            return null;
        }

        if (_baseline.Count < Options.WarmupSamples)
        {
            AddBaseline(frame.AverageLuminance);
            RememberFrame(frame);
            return null;
        }

        var baseline = _baseline.Average();
        var current = Math.Max(frame.AverageLuminance, frame.Percentile95Luminance * 0.72);
        var delta = current - baseline;
        var ratio = current / Math.Max(baseline, 0.02);
        var cooldownElapsed = frame.Metadata.CapturedAt - _lastActivation >= Options.Cooldown;
        var tileSignal = TileTransitionSignal(frame.TileSummary, EstimateMotion(frame));

        var isSpike =
            cooldownElapsed &&
            current >= Options.MinimumActivationLuminance &&
            delta >= Options.AbsoluteIncreaseThreshold &&
            ratio >= Options.RelativeIncreaseThreshold &&
            frame.BrightPixelRatio >= Options.BrightPixelRatioThreshold &&
            tileSignal.IsBroad &&
            !IsEstablishedBrightSceneContinuation(baseline, frame);
        var isLocalizedSpike = cooldownElapsed &&
            Options.LocalizedSpikeDetectionEnabled &&
            ShouldActivateLocalizedSpike(ref tileSignal, frame.TileSummary, frame.Metadata.CapturedAt);
        if (isLocalizedSpike && !isSpike && ShouldSuppressLocalizedBrightSceneSpike(baseline, frame, tileSignal))
        {
            isLocalizedSpike = false;
        }

        AddBaseline(frame.AverageLuminance);
        RememberFrame(frame);

        if (!isSpike && !isLocalizedSpike)
        {
            return null;
        }

        _pendingLocalizedSpike = null;
        _lastActivation = frame.Metadata.CapturedAt;
        var severity = isLocalizedSpike && !isSpike
            ? Math.Clamp(tileSignal.MaxTileIncrease / 0.55, 0, 1)
            : Math.Clamp(delta / 0.55, 0, 1);
        var opacity = Math.Clamp(0.22 + (severity * 0.42), 0, Options.MaximumOpacity);

        return new SpikeEvent(
            frame.Metadata.CapturedAt,
            baseline,
            current,
            isLocalizedSpike && !isSpike ? tileSignal.MaxTileIncrease : delta,
            isLocalizedSpike && !isSpike ? tileSignal.MaxTileRatio : ratio,
            severity,
            opacity,
            isLocalizedSpike && !isSpike ? tileSignal.Regions : Array.Empty<EffectRegion>());
    }

    private TileSignal TileTransitionSignal(FrameTileSummary? tileSummary, FrameTileMotion? motion)
    {
        if (tileSummary is null || tileSummary.Tiles.Count == 0)
        {
            return new TileSignal(IsBroad: true, IsLocalized: false, 0, 0, Array.Empty<EffectRegion>(), 0);
        }

        if (_previousSummary is null ||
            _previousSummary.Columns != tileSummary.Columns ||
            _previousSummary.Rows != tileSummary.Rows ||
            _previousSummary.Tiles.Count != tileSummary.Tiles.Count)
        {
            return new TileSignal(IsBroad: true, IsLocalized: false, 0, 0, Array.Empty<EffectRegion>(), 0);
        }

        var brightTiles = 0;
        var changedTiles = 0;
        var maxTileIncrease = 0.0;
        var maxTileRatio = 0.0;
        var changedTileIndexes = new List<int>();

        for (var index = 0; index < tileSummary.Tiles.Count; index++)
        {
            var tile = tileSummary.Tiles[index];
            var isBright =
                tile.AverageLuminance >= Options.BrightTileAverageThreshold ||
                tile.BrightPixelRatio >= Options.BrightTilePixelRatioThreshold ||
                tile.SignalBounds is not null;

            if (isBright)
            {
                brightTiles++;
            }

            var previousIndex = PreviousTileIndex(index, tileSummary, motion);
            if (previousIndex is null)
            {
                continue;
            }

            var previousTile = _previousSummary.Tiles[previousIndex.Value];
            var previous = Math.Max(previousTile.AverageLuminance, 0.02);
            var tileIncrease = tile.AverageLuminance - previousTile.AverageLuminance;
            var brightPixelRatioIncrease = tile.BrightPixelRatio - previousTile.BrightPixelRatio;
            var changed =
                tileIncrease >= Options.TileAverageIncreaseThreshold ||
                brightPixelRatioIncrease >= Options.TileBrightPixelRatioIncreaseThreshold;
            if (isBright && changed)
            {
                changedTiles++;
                maxTileIncrease = Math.Max(maxTileIncrease, tileIncrease);
                maxTileRatio = Math.Max(maxTileRatio, tile.AverageLuminance / previous);
                changedTileIndexes.Add(index);
            }
        }

        var totalTiles = tileSummary.Tiles.Count;
        var brightTileRatio = brightTiles / (double)totalTiles;
        var changedTileRatio = changedTiles / (double)totalTiles;
        var regionalMasks = RegionalMaskBuilder.Build(tileSummary, changedTileIndexes, RegionalMaskOptions());
        var isBroad = brightTileRatio >= Options.MinimumBrightTileRatio &&
            changedTileRatio >= Options.MinimumChangedTileRatio;
        var isLocalized = changedTiles > 0 &&
            changedTileRatio >= Math.Clamp(Options.MinimumLocalizedChangedTileRatio, 0, 1) &&
            changedTileRatio <= Math.Clamp(Options.MaximumLocalizedChangedTileRatio, 0, 1) &&
            maxTileRatio >= Options.RelativeIncreaseThreshold &&
            regionalMasks.Regions.Count > 0;

        return new TileSignal(
            isBroad,
            isLocalized,
            maxTileIncrease,
            maxTileRatio,
            regionalMasks.Regions,
            regionalMasks.TileCount / (double)totalTiles);
    }

    private FrameTileMotion? EstimateMotion(FrameAnalysisResult frame)
    {
        var summary = frame.TileSummary;
        if (summary is null ||
            _previousSummary is null ||
            _previousMetadata is null ||
            _previousMetadata.Width != frame.Metadata.Width ||
            _previousMetadata.Height != frame.Metadata.Height ||
            _previousSummary.Columns != summary.Columns ||
            _previousSummary.Rows != summary.Rows)
        {
            return null;
        }

        var frameGap = frame.Metadata.CapturedAt - _previousMetadata.CapturedAt;
        if (frameGap < TimeSpan.Zero || frameGap > PreviousTileComparisonMaximumFrameGap)
        {
            _pendingLocalizedSpike = null;
            return null;
        }

        var motion = FrameTileMotionEstimator.Estimate(_previousSummary, summary);
        if (motion.IsSceneChange || (CanCompensateMotion(motion) && motion.HasMotion))
        {
            _pendingLocalizedSpike = null;
        }

        return motion;
    }

    private bool ResetAfterUntrustedFrameGap(FrameAnalysisResult frame)
    {
        if (_previousMetadata is null)
        {
            return false;
        }

        var frameGap = frame.Metadata.CapturedAt - _previousMetadata.CapturedAt;
        if (frameGap >= TimeSpan.Zero && frameGap <= PreviousTileComparisonMaximumFrameGap)
        {
            return false;
        }

        _baseline.Clear();
        _pendingLocalizedSpike = null;
        return true;
    }

    private static int? PreviousTileIndex(int currentIndex, FrameTileSummary summary, FrameTileMotion? motion)
    {
        if (motion is not { HasMotion: true } || !CanCompensateMotion(motion))
        {
            return currentIndex;
        }

        var previousColumn = (currentIndex % summary.Columns) - motion.ColumnDelta;
        var previousRow = (currentIndex / summary.Columns) - motion.RowDelta;
        if (previousColumn < 0 ||
            previousColumn >= summary.Columns ||
            previousRow < 0 ||
            previousRow >= summary.Rows)
        {
            return null;
        }

        return (previousRow * summary.Columns) + previousColumn;
    }

    private static bool CanCompensateMotion(FrameTileMotion motion) =>
        motion.IsReliable || motion.IsMotionLikely;

    private bool ShouldActivateLocalizedSpike(
        ref TileSignal tileSignal,
        FrameTileSummary? tileSummary,
        DateTimeOffset timestamp)
    {
        if (!tileSignal.IsLocalized || tileSignal.Regions.Count == 0)
        {
            if (TryConfirmPendingLocalizedSpike(tileSummary, timestamp, out var confirmedSignal))
            {
                tileSignal = confirmedSignal;
                return true;
            }

            return false;
        }

        if (tileSignal.AffectedTileRatio >= ImmediateLocalizedSpikeMinimumTileRatio)
        {
            _pendingLocalizedSpike = null;
            return true;
        }

        if (_pendingLocalizedSpike is not null &&
            timestamp - _pendingLocalizedSpike.Timestamp <= PendingLocalizedSpikeMaximumAge &&
            RegionsOverlap(_pendingLocalizedSpike.Regions, tileSignal.Regions))
        {
            _pendingLocalizedSpike = null;
            return true;
        }

        _pendingLocalizedSpike = new LocalizedSpikeCandidate(
            timestamp,
            tileSignal.MaxTileIncrease,
            tileSignal.MaxTileRatio,
            tileSignal.Regions);
        return false;
    }

    private bool TryConfirmPendingLocalizedSpike(
        FrameTileSummary? tileSummary,
        DateTimeOffset timestamp,
        out TileSignal confirmedSignal)
    {
        confirmedSignal = new TileSignal(IsBroad: false, IsLocalized: false, 0, 0, Array.Empty<EffectRegion>(), 0);
        if (_pendingLocalizedSpike is null || tileSummary is null || tileSummary.Tiles.Count == 0)
        {
            return false;
        }

        if (timestamp - _pendingLocalizedSpike.Timestamp > PendingLocalizedSpikeMaximumAge)
        {
            _pendingLocalizedSpike = null;
            return false;
        }

        var brightTileIndexes = tileSummary.Tiles
            .Select((tile, index) => (tile, index))
            .Where(pair => IsBrightForLocalizedSpike(pair.tile))
            .Select(pair => pair.index)
            .ToArray();
        var regionalMasks = RegionalMaskBuilder.Build(tileSummary, brightTileIndexes, RegionalMaskOptions());
        if (regionalMasks.Regions.Count == 0 ||
            !RegionsOverlap(_pendingLocalizedSpike.Regions, regionalMasks.Regions))
        {
            return false;
        }

        confirmedSignal = new TileSignal(
            IsBroad: false,
            IsLocalized: true,
            _pendingLocalizedSpike.MaxTileIncrease,
            _pendingLocalizedSpike.MaxTileRatio,
            regionalMasks.Regions,
            regionalMasks.TileCount / (double)tileSummary.Tiles.Count);
        _pendingLocalizedSpike = null;
        return true;
    }

    private bool IsBrightForLocalizedSpike(TileLuminanceStats tile) =>
        tile.AverageLuminance >= Options.BrightTileAverageThreshold ||
        tile.Percentile95Luminance >= Options.MinimumActivationLuminance ||
        tile.BrightPixelRatio >= Math.Max(Options.TileBrightPixelRatioIncreaseThreshold, 0.08) ||
        (tile.SignalBounds is not null && tile.BrightPixelRatio >= Options.TileBrightPixelRatioIncreaseThreshold);

    private static bool IsEstablishedBrightSceneContinuation(double baseline, FrameAnalysisResult frame) =>
        baseline >= EstablishedBrightSceneBaselineThreshold &&
        frame.AverageLuminance >= EstablishedBrightSceneAverageThreshold &&
        frame.Percentile95Luminance >= EstablishedBrightScenePercentile95Threshold &&
        frame.BrightPixelRatio >= EstablishedBrightSceneBrightPixelRatioThreshold;

    private bool ShouldSuppressLocalizedBrightSceneSpike(
        double baseline,
        FrameAnalysisResult frame,
        TileSignal tileSignal) =>
        Options.SuppressLocalizedBrightSceneScroll &&
        tileSignal.AffectedTileRatio >= 0.01 &&
        IsEstablishedBrightSceneContinuation(baseline, frame);

    private RegionalMaskBuildOptions RegionalMaskOptions() => new()
    {
        Padding = Options.LocalizedSignalPadding,
        MergeTolerance = Options.LocalizedSignalPadding,
        MaskFit = Options.LocalizedMaskFit,
        IncludeNeighboringBrightTiles = true,
        MaximumTileRatio = Options.MaximumLocalizedChangedTileRatio,
        NeighborBrightAverageThreshold = Options.BrightTileAverageThreshold,
        NeighborBrightPercentile95Threshold = Options.MinimumActivationLuminance,
        NeighborBrightPixelRatioThreshold = Options.BrightTilePixelRatioThreshold,
        MinimumSingleTileAverageLuminance = Options.BrightTileAverageThreshold,
        MinimumSingleTilePercentile95Luminance = Options.MinimumActivationLuminance,
        MinimumSingleTileBrightPixelRatio = Math.Max(Options.TileBrightPixelRatioIncreaseThreshold, 0.10)
    };

    private static bool RegionsOverlap(IReadOnlyList<EffectRegion> first, IReadOnlyList<EffectRegion> second) =>
        first.Any(firstRegion => second.Any(secondRegion => firstRegion.TouchesOrOverlaps(secondRegion, 0.01)));

    private sealed record LocalizedSpikeCandidate(
        DateTimeOffset Timestamp,
        double MaxTileIncrease,
        double MaxTileRatio,
        IReadOnlyList<EffectRegion> Regions);

    private sealed record TileSignal(
        bool IsBroad,
        bool IsLocalized,
        double MaxTileIncrease,
        double MaxTileRatio,
        IReadOnlyList<EffectRegion> Regions,
        double AffectedTileRatio);

    private void AddBaseline(double luminance)
    {
        _baseline.Enqueue(Math.Clamp(luminance, 0, 1));
        while (_baseline.Count > Options.BaselineWindowSamples)
        {
            _baseline.Dequeue();
        }
    }

    private void RememberFrame(FrameAnalysisResult frame)
    {
        _previousSummary = frame.TileSummary;
        _previousMetadata = frame.Metadata;
    }

}
