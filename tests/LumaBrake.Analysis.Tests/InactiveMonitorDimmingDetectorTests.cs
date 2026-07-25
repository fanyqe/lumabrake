using LumaBrake.Analysis.Detection;
using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Tests;

public sealed class InactiveMonitorDimmingDetectorTests
{
    private readonly InactiveMonitorOptions _options = new()
    {
        MinimumInactiveDuration = TimeSpan.FromMilliseconds(200),
        MaximumFrameGap = TimeSpan.FromMilliseconds(500),
        MaximumAverageDelta = 0.01,
        MaximumPercentile95Delta = 0.02,
        MinimumAverageLuminance = 0.20,
        Opacity = 0.22
    };

    [Fact]
    public void Observe_DetectsUnchangedBrightFrameAfterDuration()
    {
        var detector = new InactiveMonitorDimmingDetector(_options);
        var events = Sequence([Frame(0.60, 0.82), Frame(0.60, 0.82), Frame(0.60, 0.82), Frame(0.60, 0.82)])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var inactive = Assert.Single(events);
        Assert.Equal(TimeSpan.FromMilliseconds(200), inactive!.InactiveDuration);
        Assert.Equal(_options.Opacity, inactive.RecommendedOpacity, precision: 12);
    }

    [Fact]
    public void Observe_ResetsWhenFrameChanges()
    {
        var detector = new InactiveMonitorDimmingDetector(_options);
        var events = Sequence([Frame(0.60, 0.82), Frame(0.60, 0.82), Frame(0.72, 0.90), Frame(0.72, 0.90)])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_DoesNotDimDarkUnchangedFrame()
    {
        var detector = new InactiveMonitorDimmingDetector(_options);
        var events = Sequence([Frame(0.08, 0.10), Frame(0.08, 0.10), Frame(0.08, 0.10), Frame(0.08, 0.10)])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_DoesNothingWhenDisabled()
    {
        var detector = new InactiveMonitorDimmingDetector(_options with { Enabled = false });
        var events = Sequence([Frame(0.60, 0.82), Frame(0.60, 0.82), Frame(0.60, 0.82), Frame(0.60, 0.82)])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_ResetsAfterLongFrameGap()
    {
        var detector = new InactiveMonitorDimmingDetector(_options);
        var frames = Sequence([Frame(0.60, 0.82), Frame(0.60, 0.82), Frame(0.60, 0.82)], stepMs: 100)
            .Concat(Sequence([Frame(0.60, 0.82), Frame(0.60, 0.82), Frame(0.60, 0.82)], stepMs: 100, startMs: 1000));

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    private static IEnumerable<FrameAnalysisResult> Sequence(
        IReadOnlyList<FrameAnalysisResult> frames,
        int stepMs = 100,
        int startMs = 0)
    {
        for (var index = 0; index < frames.Count; index++)
        {
            yield return frames[index] with
            {
                Metadata = new FrameMetadata(
                    DateTimeOffset.UnixEpoch.AddMilliseconds(startMs + (index * stepMs)),
                    16,
                    9,
                    "test")
            };
        }
    }

    private static FrameAnalysisResult Frame(double average, double percentile95) =>
        new(
            new FrameMetadata(DateTimeOffset.UnixEpoch, 16, 9, "test"),
            AverageLuminance: average,
            PeakLuminance: percentile95,
            Percentile95Luminance: percentile95,
            BrightPixelRatio: average >= 0.20 ? 0.50 : 0,
            TileSummary: null);
}
