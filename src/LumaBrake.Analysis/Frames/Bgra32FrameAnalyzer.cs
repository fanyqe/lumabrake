using LumaBrake.Domain.Color;
using LumaBrake.Domain.Frames;
using LumaBrake.Analysis.Luminance;

namespace LumaBrake.Analysis.Frames;

public sealed class Bgra32FrameAnalyzer
{
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
        var total = 0.0;
        var peak = 0.0;
        var brightPixels = 0;

        for (var i = 0; i < pixelCount; i++)
        {
            var offset = i * 4;
            var y = SRgbLuminance.FromRgb(new Rgb24(
                bgraPixels[offset + 2],
                bgraPixels[offset + 1],
                bgraPixels[offset]));

            luminanceValues[i] = y;
            total += y;
            peak = Math.Max(peak, y);

            if (y >= 0.72)
            {
                brightPixels++;
            }
        }

        Array.Sort(luminanceValues);
        var percentile95 = luminanceValues[(int)Math.Clamp(Math.Ceiling(pixelCount * 0.95) - 1, 0, pixelCount - 1)];

        return new FrameAnalysisResult(
            metadata,
            total / pixelCount,
            peak,
            percentile95,
            brightPixels / (double)pixelCount);
    }
}
