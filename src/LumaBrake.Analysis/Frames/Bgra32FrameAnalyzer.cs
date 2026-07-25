using LumaBrake.Domain.Color;
using LumaBrake.Domain.Frames;
using LumaBrake.Analysis.Luminance;

namespace LumaBrake.Analysis.Frames;

public sealed class Bgra32FrameAnalyzer
{
    private const double BrightPixelThreshold = 0.72;
    private readonly int _tileColumns;
    private readonly int _tileRows;

    public Bgra32FrameAnalyzer(int tileColumns = 16, int tileRows = 9)
    {
        if (tileColumns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tileColumns), "Tile columns must be positive.");
        }

        if (tileRows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tileRows), "Tile rows must be positive.");
        }

        _tileColumns = tileColumns;
        _tileRows = tileRows;
    }

    public FrameAnalysisResult Analyze(ReadOnlySpan<byte> bgraPixels, FrameMetadata metadata)
    {
        if (metadata.Width <= 0 || metadata.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(metadata), "Frame dimensions must be positive.");
        }

        var pixelCount = checked(metadata.Width * metadata.Height);
        if (bgraPixels.Length < pixelCount * 4)
        {
            throw new ArgumentException("Pixel buffer is smaller than the declared frame.", nameof(bgraPixels));
        }

        var luminanceValues = new double[pixelCount];
        var tileColumns = Math.Min(_tileColumns, metadata.Width);
        var tileRows = Math.Min(_tileRows, metadata.Height);
        var tileValues = CreateTileBuckets(tileColumns * tileRows);
        var total = 0.0;
        var peak = 0.0;
        var brightPixels = 0;

        for (var yPosition = 0; yPosition < metadata.Height; yPosition++)
        {
            for (var xPosition = 0; xPosition < metadata.Width; xPosition++)
            {
                var pixelIndex = (yPosition * metadata.Width) + xPosition;
                var offset = pixelIndex * 4;
                var y = SRgbLuminance.FromRgb(new Rgb24(
                    bgraPixels[offset + 2],
                    bgraPixels[offset + 1],
                    bgraPixels[offset]));

                luminanceValues[pixelIndex] = y;
                total += y;
                peak = Math.Max(peak, y);

                if (y >= BrightPixelThreshold)
                {
                    brightPixels++;
                }

                var tileColumn = Math.Min((xPosition * tileColumns) / metadata.Width, tileColumns - 1);
                var tileRow = Math.Min((yPosition * tileRows) / metadata.Height, tileRows - 1);
                tileValues[(tileRow * tileColumns) + tileColumn].Add(y);
            }
        }

        Array.Sort(luminanceValues);
        var percentile95 = Percentile95(luminanceValues);

        return new FrameAnalysisResult(
            metadata,
            total / pixelCount,
            peak,
            percentile95,
            brightPixels / (double)pixelCount,
            CreateTileSummary(tileColumns, tileRows, tileValues));
    }

    private static List<double>[] CreateTileBuckets(int tileCount)
    {
        var buckets = new List<double>[tileCount];
        for (var index = 0; index < tileCount; index++)
        {
            buckets[index] = [];
        }

        return buckets;
    }

    private static FrameTileSummary CreateTileSummary(int columns, int rows, IReadOnlyList<List<double>> tileValues)
    {
        var tiles = new List<TileLuminanceStats>(tileValues.Count);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var values = tileValues[(row * columns) + column];
                values.Sort();
                var brightPixels = values.Count(value => value >= BrightPixelThreshold);
                tiles.Add(new TileLuminanceStats(
                    column,
                    row,
                    values.Average(),
                    Percentile95(values),
                    brightPixels / (double)values.Count));
            }
        }

        return new FrameTileSummary(columns, rows, tiles);
    }

    private static double Percentile95(IReadOnlyList<double> sortedValues)
    {
        var index = (int)Math.Clamp(Math.Ceiling(sortedValues.Count * 0.95) - 1, 0, sortedValues.Count - 1);
        return sortedValues[index];
    }
}
