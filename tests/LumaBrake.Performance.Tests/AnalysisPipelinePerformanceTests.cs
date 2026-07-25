using System.Diagnostics;
using LumaBrake.Analysis.Detection;
using LumaBrake.Analysis.Frames;
using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Frames;
using LumaBrake.Effects.Composition;

namespace LumaBrake.Performance.Tests;

public sealed class AnalysisPipelinePerformanceTests
{
    private const int Width = 160;
    private const int Height = 90;
    private const int MeasuredFrameCount = 240;
    private const int LongRunFrameCount = 1_800;

#if DEBUG
    private static readonly TimeSpan MaximumMeasuredDuration = TimeSpan.FromSeconds(6);
#else
    private static readonly TimeSpan MaximumMeasuredDuration = TimeSpan.FromSeconds(4);
#endif

    [Fact]
    public void AnalyzeAndComposeSyntheticFrames_StaysWithinCiSmokeBudget()
    {
        var frames = CreateSyntheticFrames();
        var pipeline = new SyntheticAnalysisPipeline();

        for (var index = 0; index < frames.Count; index++)
        {
            pipeline.Observe(frames[index], Metadata(index));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        FinalEffectState final = FinalEffectState.Clear;

        for (var index = 0; index < MeasuredFrameCount; index++)
        {
            final = pipeline.Observe(frames[index % frames.Count], Metadata(index));
        }

        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var latencyPerFrameMs = stopwatch.Elapsed.TotalMilliseconds / MeasuredFrameCount;
        var allocatedPerFrame = allocatedBytes / (double)MeasuredFrameCount;

        Assert.True(
            stopwatch.Elapsed <= MaximumMeasuredDuration,
            $"Synthetic analysis pipeline took {stopwatch.Elapsed.TotalMilliseconds:0.0} ms " +
            $"({latencyPerFrameMs:0.00} ms/frame), budget {MaximumMeasuredDuration.TotalMilliseconds:0.0} ms.");
        Assert.True(
            allocatedPerFrame <= 2_500_000,
            $"Synthetic analysis pipeline allocated {allocatedPerFrame:0} bytes/frame, budget 2500000 bytes/frame.");
        Assert.InRange(final.GlobalOpacity, 0, 0.92);
    }

    [Fact]
    public void Detectors_DoNotRetainUnboundedStateAcrossLongSyntheticRun()
    {
        var frames = CreateSyntheticFrames();
        var pipeline = new SyntheticAnalysisPipeline();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var retainedBefore = GC.GetTotalMemory(forceFullCollection: true);

        for (var index = 0; index < LongRunFrameCount; index++)
        {
            pipeline.Observe(frames[index % frames.Count], Metadata(index));
        }

        var retainedAfter = GC.GetTotalMemory(forceFullCollection: true);
        var retainedGrowth = retainedAfter - retainedBefore;

        Assert.True(
            retainedGrowth <= 8 * 1024 * 1024,
            $"Synthetic long run retained {retainedGrowth} bytes, budget {8 * 1024 * 1024} bytes.");
    }

    private static FrameMetadata Metadata(int frameIndex) =>
        new(DateTimeOffset.UnixEpoch.AddMilliseconds(frameIndex * 66), Width, Height, "performance");

    private static IReadOnlyList<byte[]> CreateSyntheticFrames()
    {
        var frames = new List<byte[]>();
        for (var index = 0; index < 12; index++)
        {
            frames.Add(CreateSyntheticFrame(index));
        }

        return frames;
    }

    private static byte[] CreateSyntheticFrame(int frameIndex)
    {
        var pixels = new byte[Width * Height * 4];
        var background = frameIndex % 4 < 2 ? (byte)32 : (byte)176;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = background;
            pixels[offset + 1] = background;
            pixels[offset + 2] = background;
            pixels[offset + 3] = 255;
        }

        var blockX = (frameIndex % 8) * 16;
        var blockY = (frameIndex % 3) * 20;
        FillRect(pixels, blockX, blockY, 32, 18, 245, 245, 245);

        if (frameIndex % 2 == 0)
        {
            FillRect(pixels, 112, 48, 24, 24, 220, 16, 16);
        }

        return pixels;
    }

    private static void FillRect(
        byte[] pixels,
        int x,
        int y,
        int width,
        int height,
        byte red,
        byte green,
        byte blue)
    {
        for (var yPosition = y; yPosition < Math.Min(y + height, Height); yPosition++)
        {
            for (var xPosition = x; xPosition < Math.Min(x + width, Width); xPosition++)
            {
                var offset = ((yPosition * Width) + xPosition) * 4;
                pixels[offset] = blue;
                pixels[offset + 1] = green;
                pixels[offset + 2] = red;
                pixels[offset + 3] = 255;
            }
        }
    }

    private sealed class SyntheticAnalysisPipeline
    {
        private readonly Bgra32FrameAnalyzer _analyzer = new();
        private readonly BrightnessSpikeDetector _spike = new(new SpikeDetectorOptions());
        private readonly RepetitiveFlashDetector _flash = new(new FlashDetectorOptions());
        private readonly StaticShadeDetector _staticShade = new(StaticShadeOptions.ForPreset(StaticShadePreset.StaticHud));
        private readonly AdaptiveContentDimmingDetector _adaptive = new(new AdaptiveContentOptions());
        private readonly InactiveMonitorDimmingDetector _inactive = new(new InactiveMonitorOptions());
        private readonly EffectComposer _composer = new();

        public FinalEffectState Observe(byte[] frame, FrameMetadata metadata)
        {
            var analysis = _analyzer.Analyze(frame, metadata);
            var requests = new List<EffectRequest>(capacity: 5);

            if (_spike.Observe(analysis) is { } spike)
            {
                requests.Add(new EffectRequest(
                    EffectReason.BrightnessSpike,
                    spike.RecommendedOpacity,
                    spike.Timestamp,
                    TimeSpan.FromMilliseconds(250),
                    TimeSpan.FromMilliseconds(750)));
            }

            if (_flash.Observe(analysis) is { } flash)
            {
                requests.Add(new EffectRequest(
                    EffectReason.RepetitiveFlash,
                    flash.RecommendedOpacity,
                    flash.Timestamp,
                    TimeSpan.FromMilliseconds(320),
                    TimeSpan.FromMilliseconds(950),
                    flash.AffectedRegions));
            }

            if (_staticShade.Observe(analysis) is { } staticShade)
            {
                requests.Add(new EffectRequest(
                    EffectReason.StaticShade,
                    staticShade.RecommendedOpacity,
                    staticShade.Timestamp,
                    TimeSpan.FromMilliseconds(500),
                    TimeSpan.FromMilliseconds(1200),
                    staticShade.AffectedRegions));
            }

            if (_adaptive.Observe(analysis) is { } adaptive)
            {
                requests.Add(new EffectRequest(
                    EffectReason.AdaptiveContent,
                    adaptive.RecommendedOpacity,
                    adaptive.Timestamp,
                    TimeSpan.FromMilliseconds(600),
                    TimeSpan.FromMilliseconds(1500)));
            }

            if (_inactive.Observe(analysis) is { } inactive)
            {
                requests.Add(new EffectRequest(
                    EffectReason.InactiveMonitor,
                    inactive.RecommendedOpacity,
                    inactive.Timestamp,
                    TimeSpan.FromMilliseconds(500),
                    TimeSpan.FromMilliseconds(900)));
            }

            return _composer.Compose(requests, userMaximumOpacity: 0.64);
        }
    }
}
