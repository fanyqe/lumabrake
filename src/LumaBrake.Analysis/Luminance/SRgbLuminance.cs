using LumaBrake.Domain.Color;

namespace LumaBrake.Analysis.Luminance;

public static class SRgbLuminance
{
    public static double FromRgb(Rgb24 color)
    {
        var red = ToLinear(color.R / 255.0);
        var green = ToLinear(color.G / 255.0);
        var blue = ToLinear(color.B / 255.0);

        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    public static double ToLinear(double srgb)
    {
        if (double.IsNaN(srgb) || double.IsInfinity(srgb))
        {
            throw new ArgumentOutOfRangeException(nameof(srgb), "sRGB channel must be finite.");
        }

        var c = Math.Clamp(srgb, 0, 1);
        return c <= 0.04045
            ? c / 12.92
            : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
