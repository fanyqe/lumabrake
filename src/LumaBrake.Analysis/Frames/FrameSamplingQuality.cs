using LumaBrake.Domain.Frames;

namespace LumaBrake.Analysis.Frames;

public static class FrameSamplingQuality
{
    public static bool IsNearEmpty(ReadOnlySpan<byte> bgraPixels, FrameMetadata metadata)
    {
        if (metadata.Width <= 0 || metadata.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(metadata), "Frame dimensions must be positive.");
        }

        var byteCount = checked(metadata.Width * metadata.Height * 4);
        if (bgraPixels.Length < byteCount)
        {
            throw new ArgumentException("Pixel buffer is smaller than the declared frame.", nameof(bgraPixels));
        }

        for (var index = 0; index < byteCount; index++)
        {
            if (bgraPixels[index] != 0)
            {
                return false;
            }
        }

        return true;
    }
}
