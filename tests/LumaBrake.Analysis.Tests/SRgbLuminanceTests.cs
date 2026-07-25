using LumaBrake.Analysis.Luminance;
using LumaBrake.Domain.Color;

namespace LumaBrake.Analysis.Tests;

public sealed class SRgbLuminanceTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(255, 1)]
    [InlineData(128, 0.21586050011389926)]
    public void ToLinear_UsesSRgbTransferFunction(byte channel, double expected)
    {
        var actual = SRgbLuminance.ToLinear(channel / 255.0);

        Assert.Equal(expected, actual, precision: 12);
    }

    [Theory]
    [InlineData(255, 0, 0, 0.2126)]
    [InlineData(0, 255, 0, 0.7152)]
    [InlineData(0, 0, 255, 0.0722)]
    [InlineData(255, 255, 255, 1)]
    [InlineData(0, 0, 0, 0)]
    public void FromRgb_UsesRelativeLuminanceWeights(byte red, byte green, byte blue, double expected)
    {
        var actual = SRgbLuminance.FromRgb(new Rgb24(red, green, blue));

        Assert.Equal(expected, actual, precision: 12);
    }

    [Fact]
    public void ToLinear_RejectsNonFiniteInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SRgbLuminance.ToLinear(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => SRgbLuminance.ToLinear(double.PositiveInfinity));
    }
}
