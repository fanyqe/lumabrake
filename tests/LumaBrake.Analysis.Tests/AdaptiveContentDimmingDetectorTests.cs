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
    public void Observe_DefaultOptionsEmitBeforeHalfSecondBrightScene()
    {
        var detector = new AdaptiveContentDimmingDetector(new AdaptiveContentOptions());
        var events = Sequence([Bright(), Bright(), Bright(), Bright()], stepMs: 150)
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var adaptive = Assert.Single(events);
        Assert.Equal(TimeSpan.FromMilliseconds(450), adaptive!.BrightDuration);
        Assert.InRange(adaptive.RecommendedOpacity, new AdaptiveContentOptions().MinimumOpacity, new AdaptiveContentOptions().MaximumOpacity);
    }

    [Fact]
    public void Observe_KeepsOpacityStableForSmallBrightnessOscillation()
    {
        var detector = new AdaptiveContentDimmingDetector(_options);
        var events = Sequence(
            [
                Bright(0.70, 0.88, 0.72),
                Bright(0.72, 0.90, 0.74),
                Bright(0.71, 0.89, 0.73),
                Bright(0.72, 0.90, 0.74),
                Bright(0.70, 0.88, 0.72),
                Bright(0.71, 0.89, 0.73)
            ])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.True(events.Length >= 3);
        var opacities = events.Select(result => result!.RecommendedOpacity).ToArray();
        Assert.True(opacities.Max() - opacities.Min() <= 0.01);
    }

    [Fact]
    public void Observe_EmitsForBroadBrightPageWithModerateAverage()
    {
        var detector = new AdaptiveContentDimmingDetector(_options);
        var events = Sequence([BroadWhitePage(), BroadWhitePage(), BroadWhitePage(), BroadWhitePage()])
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        var adaptive = Assert.Single(events);
        Assert.Equal(TimeSpan.FromMilliseconds(300), adaptive!.BrightDuration);
        Assert.True(adaptive.RecommendedOpacity >= _options.MinimumOpacity);
    }

    [Fact]
    public void Observe_MaintainsDimmingThroughBorderlineBrightFrames()
    {
        var detector = new AdaptiveContentDimmingDetector(_options);
        var frames = Sequence(
            [
                Bright(),
                Bright(),
                Bright(),
                Bright(),
                BorderlineBrightPage(),
                BorderlineBrightPage(),
                BorderlineBrightPage()
            ]);

        var events = frames
            .Select(detector.Observe)
            .Where(result => result is not null)
            .ToArray();

        Assert.True(events.Length >= 4);
        Assert.All(events, adaptive => Assert.True(adaptive!.RecommendedOpacity >= _options.MinimumOpacity));
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

    private static FrameAnalysisResult Bright(
        double average = 0.72,
        double percentile95 = 0.90,
        double brightRatio = 0.80) =>
        Frame(average, percentile95, brightRatio);

    private static FrameAnalysisResult BroadWhitePage() =>
        Frame(average: 0.33, percentile95: 0.90, brightRatio: 0.56);

    private static FrameAnalysisResult BorderlineBrightPage() =>
        Frame(average: 0.30, percentile95: 0.58, brightRatio: 0.22);

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
