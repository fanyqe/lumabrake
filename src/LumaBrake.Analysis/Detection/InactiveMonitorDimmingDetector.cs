using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Detection;

public sealed class InactiveMonitorDimmingDetector
{
    private DateTimeOffset? _inactiveSince;
    private DateTimeOffset? _lastSeen;
    private double? _previousAverage;
    private double? _previousP95;
    private FrameTileSummary? _previousTileSummary;

    public InactiveMonitorDimmingDetector(InactiveMonitorOptions options)
    {
        Options = options;
    }

    public InactiveMonitorOptions Options { get; }

    public InactiveMonitorEvent? Observe(FrameAnalysisResult frame)
    {
        if (!Options.Enabled)
        {
            Reset();
            return null;
        }

        if (_lastSeen is not null)
        {
            var gap = frame.Metadata.CapturedAt - _lastSeen.Value;
            if (gap < TimeSpan.Zero || gap > Options.MaximumFrameGap)
            {
                Reset();
            }
        }

        var isBrightEnough = frame.AverageLuminance >= Options.MinimumAverageLuminance;
        var isUnchanged = IsUnchanged(frame);
        _lastSeen = frame.Metadata.CapturedAt;
        _previousAverage = frame.AverageLuminance;
        _previousP95 = frame.Percentile95Luminance;
        _previousTileSummary = frame.TileSummary;

        if (!isBrightEnough || !isUnchanged)
        {
            _inactiveSince = null;
            return null;
        }

        _inactiveSince ??= frame.Metadata.CapturedAt;
        var inactiveDuration = frame.Metadata.CapturedAt - _inactiveSince.Value;
        if (inactiveDuration < Options.MinimumInactiveDuration)
        {
            return null;
        }

        return new InactiveMonitorEvent(
            frame.Metadata.CapturedAt,
            inactiveDuration,
            Math.Clamp(Options.Opacity, 0, 0.92));
    }

    private bool IsUnchanged(FrameAnalysisResult frame)
    {
        if (_previousAverage is null || _previousP95 is null)
        {
            return false;
        }

        var globallyStable =
            Math.Abs(frame.AverageLuminance - _previousAverage.Value) <= Options.MaximumAverageDelta &&
            Math.Abs(frame.Percentile95Luminance - _previousP95.Value) <= Options.MaximumPercentile95Delta;

        return globallyStable && !HasMeaningfulTileActivity(frame.TileSummary);
    }

    private bool HasMeaningfulTileActivity(FrameTileSummary? current)
    {
        if (_previousTileSummary is null || current is null)
        {
            return false;
        }

        if (_previousTileSummary.Columns != current.Columns ||
            _previousTileSummary.Rows != current.Rows ||
            _previousTileSummary.Tiles.Count != current.Tiles.Count)
        {
            return true;
        }

        var changedTiles = 0;
        for (var index = 0; index < current.Tiles.Count; index++)
        {
            var previousTile = _previousTileSummary.Tiles[index];
            var currentTile = current.Tiles[index];
            if (Math.Abs(currentTile.AverageLuminance - previousTile.AverageLuminance) > Options.TileChangeAverageDelta ||
                Math.Abs(currentTile.Percentile95Luminance - previousTile.Percentile95Luminance) > Options.TileChangePercentile95Delta)
            {
                changedTiles++;
            }
        }

        var changedTileRatio = changedTiles / (double)current.Tiles.Count;
        return changedTileRatio > Math.Clamp(Options.MaximumChangedTileRatio, 0, 1);
    }

    public void Reset()
    {
        _inactiveSince = null;
        _lastSeen = null;
        _previousAverage = null;
        _previousP95 = null;
        _previousTileSummary = null;
    }
}
