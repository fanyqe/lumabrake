using LumaBrake.Analysis.Detection;
using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Tests;

public sealed class AdaptiveContentDimmingDetectorTests
{
    private readonly AdaptiveContentOptions _options = new()
    {
        MinimumBrightDuration = TimeSpan.FromMilliseconds(300),
        MaximumFrameGap = TimeSpan.FromMilliseconds(500),
        MinimumAverageLuminance = 0.40,
        MinimumPercentile95Luminance = 0.70,
        MinimumBrightPixelRatio = 0.35,
        MinimumOpacity = 0.05,
        MaximumOpacity = 0.24
    };

    [Fact]
    public void Observe_EmitsAfterBrightScenePersists()
    {
        var detector = new AdaptiveContentDimmingDetector(_options);
        var events = Sequence([Bright(), Bright(), Bright(), Bright()])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var adaptive = Assert.Single(events);
        Assert.Equal(TimeSpan.FromMilliseconds(300), adaptive!.BrightDuration);
        Assert.InRange(adaptive.RecommendedOpacity, _options.MinimumOpacity, _options.MaximumOpacity);
    }

    [Fact]
    public void Observe_DoesNotEmitForBriefBrightScene()
    {
        var detector = new AdaptiveContentDimmingDetector(_options);
        var events = Sequence([Bright(), Bright(), Bright()])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_ResetsWhenSceneTurnsDark()
    {
        var detector = new AdaptiveContentDimmingDetector(_options);
        var events = Sequence([Bright(), Bright(), Dark(), Bright(), Bright(), Bright()])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_DoesNothingWhenDisabled()
    {
        var detector = new AdaptiveContentDimmingDetector(_options with { Enabled = false });
        var events = Sequence([Bright(), Bright(), Bright(), Bright()])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.Empty(events);
    }

    [Fact]
    public void Observe_ResetsAfterLongFrameGap()
    {
        var detector = new AdaptiveContentDimmingDetector(_options);
        var frames = Sequence([Bright(), Bright(), Bright()], stepMs: 100)
            .Concat(Sequence([Bright(), Bright(), Bright()], stepMs: 100, startMs: 1000));

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

    private static FrameAnalysisResult Bright() =>
        Frame(average: 0.72, percentile95: 0.90, brightRatio: 0.80);

    private static FrameAnalysisResult Dark() =>
        Frame(average: 0.18, percentile95: 0.28, brightRatio: 0);

    private static FrameAnalysisResult Frame(double average, double percentile95, double brightRatio) =>
        new(
            new FrameMetadata(DateTimeOffset.UnixEpoch, 16, 9, "test"),
            AverageLuminance: average,
            PeakLuminance: percentile95,
            Percentile95Luminance: percentile95,
            BrightPixelRatio: brightRatio,
            TileSummary: null);
}
