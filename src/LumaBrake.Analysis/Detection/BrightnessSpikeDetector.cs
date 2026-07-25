using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Detection;

public sealed class BrightnessSpikeDetector
{
    private readonly Queue<double> _baseline = new();
    private double[]? _previousTileAverages;
    private DateTimeOffset _lastActivation = DateTimeOffset.MinValue;

    public BrightnessSpikeDetector(SpikeDetectorOptions options)
    {
        Options = options;
    }

    public SpikeDetectorOptions Options { get; }

    public SpikeEvent? Observe(FrameAnalysisResult frame)
    {
        if (_baseline.Count < Options.WarmupSamples)
        {
            AddBaseline(frame.AverageLuminance);
            RememberTiles(frame.TileSummary);
            return null;
        }

        var baseline = _baseline.Average();
        var current = Math.Max(frame.AverageLuminance, frame.Percentile95Luminance * 0.72);
        var delta = current - baseline;
        var ratio = current / Math.Max(baseline, 0.02);
        var cooldownElapsed = frame.Metadata.CapturedAt - _lastActivation >= Options.Cooldown;
        var tileSignal = IsBroadTileTransition(frame.TileSummary);

        var isSpike =
            cooldownElapsed &&
            current >= Options.MinimumActivationLuminance &&
            delta >= Options.AbsoluteIncreaseThreshold &&
            ratio >= Options.RelativeIncreaseThreshold &&
            frame.BrightPixelRatio >= Options.BrightPixelRatioThreshold &&
            tileSignal;

        AddBaseline(frame.AverageLuminance);
        RememberTiles(frame.TileSummary);

        if (!isSpike)
        {
            return null;
        }

        _lastActivation = frame.Metadata.CapturedAt;
        var severity = Math.Clamp(delta / 0.55, 0, 1);
        var opacity = Math.Clamp(0.22 + (severity * 0.42), 0, Options.MaximumOpacity);

        return new SpikeEvent(
            frame.Metadata.CapturedAt,
            baseline,
            current,
            delta,
            ratio,
            severity,
            opacity);
    }

    private bool IsBroadTileTransition(FrameTileSummary? tileSummary)
    {
        if (tileSummary is null || tileSummary.Tiles.Count == 0)
        {
            return true;
        }

        if (_previousTileAverages is null || _previousTileAverages.Length != tileSummary.Tiles.Count)
        {
            return true;
        }

        var brightTiles = 0;
        var changedTiles = 0;

        for (var index = 0; index < tileSummary.Tiles.Count; index++)
        {
            var tile = tileSummary.Tiles[index];
            var isBright =
                tile.AverageLuminance >= Options.BrightTileAverageThreshold ||
                tile.BrightPixelRatio >= Options.BrightTilePixelRatioThreshold;

            if (isBright)
            {
                brightTiles++;
            }

            if (isBright && tile.AverageLuminance - _previousTileAverages[index] >= Options.TileAverageIncreaseThreshold)
            {
                changedTiles++;
            }
        }

        var totalTiles = tileSummary.Tiles.Count;
        return brightTiles / (double)totalTiles >= Options.MinimumBrightTileRatio &&
            changedTiles / (double)totalTiles >= Options.MinimumChangedTileRatio;
    }

    private void AddBaseline(double luminance)
    {
        _baseline.Enqueue(Math.Clamp(luminance, 0, 1));
        while (_baseline.Count > Options.BaselineWindowSamples)
        {
            _baseline.Dequeue();
        }
    }

    private void RememberTiles(FrameTileSummary? tileSummary)
    {
        _previousTileAverages = tileSummary?.Tiles.Select(tile => tile.AverageLuminance).ToArray();
    }
}
