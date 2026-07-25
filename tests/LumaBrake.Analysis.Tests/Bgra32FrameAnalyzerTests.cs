using LumaBrake.Analysis.Frames;
using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Tests;

public sealed class Bgra32FrameAnalyzerTests
{
    private readonly Bgra32FrameAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_ReturnsOneForWhiteFrame()
    {
        var pixels = SolidBgra(4, 4, 255, 255, 255);
        var result = _analyzer.Analyze(pixels, Metadata(4, 4));

        Assert.Equal(1, result.AverageLuminance, precision: 12);
        Assert.Equal(1, result.Percentile95Luminance, precision: 12);
        Assert.Equal(1, result.BrightPixelRatio, precision: 12);
    }

    [Fact]
    public void Analyze_DoesNotTreatEncodedGrayAsLinearGray()
    {
        var pixels = SolidBgra(4, 4, 128, 128, 128);
        var result = _analyzer.Analyze(pixels, Metadata(4, 4));

        Assert.InRange(result.AverageLuminance, 0.2158, 0.2159);
    }

    [Fact]
    public void Analyze_RejectsShortBuffer()
    {
        Assert.Throws<ArgumentException>(() => _analyzer.Analyze([0, 0, 0], Metadata(2, 2)));
    }

    private static FrameMetadata Metadata(int width, int height) =>
        new(DateTimeOffset.UnixEpoch, width, height, "test");

    private static byte[] SolidBgra(int width, int height, byte red, byte green, byte blue)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = blue;
            pixels[i + 1] = green;
            pixels[i + 2] = red;
            pixels[i + 3] = 255;
        }

        return pixels;
    }
}
