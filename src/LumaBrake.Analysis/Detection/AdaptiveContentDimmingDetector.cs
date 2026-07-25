using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Detection;

public sealed class AdaptiveContentDimmingDetector
{
    private const double MaintenanceScoreMultiplier = 0.72;
    private const double MaintenancePercentileMultiplier = 0.78;
    private const double MaintenanceBrightPixelRatioMultiplier = 0.58;
    private DateTimeOffset? _brightSince;
    private DateTimeOffset? _lastSeen;
    private double _smoothedOpacity;

    public AdaptiveContentDimmingDetector(AdaptiveContentOptions options)
    {
        Options = options;
    }

    public AdaptiveContentOptions Options { get; }

    public AdaptiveContentEvent? Observe(FrameAnalysisResult frame)
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

        _lastSeen = frame.Metadata.CapturedAt;

        var isBrightScene = IsBrightScene(frame);
        if (!isBrightScene && !ShouldMaintainDimming(frame))
        {
            _brightSince = null;
            _smoothedOpacity = 0;
            return null;
        }

        _brightSince ??= frame.Metadata.CapturedAt;
        var brightDuration = frame.Metadata.CapturedAt - _brightSince.Value;
        if (brightDuration < Options.MinimumBrightDuration && _smoothedOpacity <= 0)
        {
            return null;
        }

        var targetOpacity = TargetOpacity(frame);
        if (_smoothedOpacity <= 0)
        {
            _smoothedOpacity = targetOpacity;
        }
        else if (Math.Abs(targetOpacity - _smoothedOpacity) > Options.DeadZone)
        {
            var factor = Math.Clamp(Options.SmoothingFactor, 0.01, 1);
            _smoothedOpacity += (targetOpacity - _smoothedOpacity) * factor;
        }

        var opacity = Math.Clamp(_smoothedOpacity, 0, Options.MaximumOpacity);
        if (opacity < Options.MinimumOpacity)
        {
            return null;
        }

        return new AdaptiveContentEvent(
            frame.Metadata.CapturedAt,
            BrightnessScore(frame),
            brightDuration,
            opacity);
    }

    private void Reset()
    {
        _brightSince = null;
        _lastSeen = null;
        _smoothedOpacity = 0;
    }

    private bool IsBrightScene(FrameAnalysisResult frame)
    {
        var hasBroadBrightArea =
            frame.AverageLuminance >= Options.MinimumAverageLuminance ||
            frame.BrightPixelRatio >= Options.MinimumBrightPixelRatio;
        var hasBrightHighlights = frame.Percentile95Luminance >= Options.MinimumPercentile95Luminance;

        return BrightnessScore(frame) >= Options.MinimumAverageLuminance &&
            hasBroadBrightArea &&
            hasBrightHighlights;
    }

    private bool ShouldMaintainDimming(FrameAnalysisResult frame)
    {
        if (_smoothedOpacity < Options.MinimumOpacity)
        {
            return false;
        }

        return BrightnessScore(frame) >= Options.MinimumAverageLuminance * MaintenanceScoreMultiplier ||
            frame.Percentile95Luminance >= Options.MinimumPercentile95Luminance * MaintenancePercentileMultiplier ||
            frame.BrightPixelRatio >= Options.MinimumBrightPixelRatio * MaintenanceBrightPixelRatioMultiplier;
    }

    private double TargetOpacity(FrameAnalysisResult frame)
    {
        var score = BrightnessScore(frame);
        var severity = Math.Clamp((score - Options.MinimumAverageLuminance) / 0.45, 0, 1);
        return Math.Clamp(
            Options.MinimumOpacity + (severity * (Options.MaximumOpacity - Options.MinimumOpacity)),
            Options.MinimumOpacity,
            Options.MaximumOpacity);
    }

    private static double BrightnessScore(FrameAnalysisResult frame) =>
        Math.Max(
            frame.AverageLuminance,
            Math.Max(frame.Percentile95Luminance * 0.68, frame.BrightPixelRatio));
}
