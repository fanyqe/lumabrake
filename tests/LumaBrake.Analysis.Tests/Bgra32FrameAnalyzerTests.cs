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

    [Fact]
    public void Analyze_ReturnsTileStatisticsForConfiguredGrid()
    {
        var analyzer = new Bgra32FrameAnalyzer(tileColumns: 2, tileRows: 2);
        var pixels = SolidBgra(4, 4, 0, 0, 0);
        FillRectangle(pixels, 4, left: 0, top: 0, width: 2, height: 2, red: 255, green: 255, blue: 255);

        var result = analyzer.Analyze(pixels, Metadata(4, 4));

        Assert.NotNull(result.TileSummary);
        Assert.Equal(2, result.TileSummary.Columns);
        Assert.Equal(2, result.TileSummary.Rows);
        Assert.Equal(4, result.TileSummary.Tiles.Count);
        Assert.Equal(1, result.TileSummary.Tiles[0].AverageLuminance, precision: 12);
        Assert.Equal(0, result.TileSummary.Tiles[1].AverageLuminance, precision: 12);
        Assert.Equal(0.25, result.BrightPixelRatio, precision: 12);
    }

    [Fact]
    public void Analyze_ReportsLowTileCoverageForSmallBrightRegion()
    {
        var analyzer = new Bgra32FrameAnalyzer(tileColumns: 4, tileRows: 4);
        var pixels = SolidBgra(8, 8, 0, 0, 0);
        FillRectangle(pixels, 8, left: 0, top: 0, width: 2, height: 2, red: 255, green: 255, blue: 255);

        var result = analyzer.Analyze(pixels, Metadata(8, 8));
        var brightTileRatio = result.TileSummary!.Tiles.Count(tile => tile.AverageLuminance >= 0.35) /
            (double)result.TileSummary.Tiles.Count;

        Assert.Equal(0.0625, result.BrightPixelRatio, precision: 12);
        Assert.Equal(0.0625, brightTileRatio, precision: 12);
        Assert.Equal(1, result.Percentile95Luminance, precision: 12);
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

    private static void FillRectangle(
        byte[] pixels,
        int frameWidth,
        int left,
        int top,
        int width,
        int height,
        byte red,
        byte green,
        byte blue)
    {
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                var offset = ((y * frameWidth) + x) * 4;
                pixels[offset] = blue;
                pixels[offset + 1] = green;
                pixels[offset + 2] = red;
                pixels[offset + 3] = 255;
            }
        }
    }
}
