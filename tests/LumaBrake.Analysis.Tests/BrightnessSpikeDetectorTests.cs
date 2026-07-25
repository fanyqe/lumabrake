using LumaBrake.Analysis.Detection;
using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Tests;

public sealed class BrightnessSpikeDetectorTests
{
    private readonly SpikeDetectorOptions _options = new()
    {
        WarmupSamples = 4,
        BaselineWindowSamples = 8,
        MinimumActivationLuminance = 0.25,
        AbsoluteIncreaseThreshold = 0.16,
        RelativeIncreaseThreshold = 1.8,
        BrightPixelRatioThreshold = 0.10,
        Cooldown = TimeSpan.FromMilliseconds(500)
    };

    [Fact]
    public void Observe_DetectsAbruptDarkToBrightTransition()
    {
        var detector = new BrightnessSpikeDetector(_options);
        SpikeEvent? detected = null;

        foreach (var frame in Sequence([0.08, 0.09, 0.08, 0.09, 0.86]))
        {
            detected = detector.Observe(frame);
        }

        Assert.NotNull(detected);
        Assert.InRange(detected.Delta, 0.70, 0.80);
        Assert.InRange(detected.RecommendedOpacity, 0.22, _options.MaximumOpacity);
    }

    [Fact]
    public void Observe_DoesNotTriggerDuringWarmup()
    {
        var detector = new BrightnessSpikeDetector(_options);
        var first = Sequence([0.9]).Single();

        Assert.Null(detector.Observe(first));
    }

    [Fact]
    public void Observe_DoesNotTriggerOnGradualRamp()
    {
        var detector = new BrightnessSpikeDetector(_options);
        var events = Sequence([0.10, 0.12, 0.14, 0.16, 0.20, 0.24, 0.30, 0.36, 0.42])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_RespectsCooldown()
    {
        var detector = new BrightnessSpikeDetector(_options);
        var events = Sequence([0.08, 0.09, 0.08, 0.09, 0.86, 0.08, 0.86], 100)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Single(events);
    }

    private static IEnumerable<FrameAnalysisResult> Sequence(IReadOnlyList<double> luminance, int stepMs = 100)
    {
        for (var i = 0; i < luminance.Count; i++)
        {
            var value = luminance[i];
            yield return new FrameAnalysisResult(
                new FrameMetadata(DateTimeOffset.UnixEpoch.AddMilliseconds(i * stepMs), 1, 1, "test"),
                value,
                value,
                value,
                value >= 0.7 ? 1 : 0);
        }
    }
}
