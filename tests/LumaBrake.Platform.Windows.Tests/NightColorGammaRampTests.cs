using LumaBrake.Platform.Windows;

namespace LumaBrake.Platform.Windows.Tests;

public sealed class NightColorGammaRampTests
{
    [Fact]
    public void IdentityRamp_ReachesFullNativeWhite()
    {
        Assert.Equal(ushort.MaxValue, GammaRamp.Identity.Values[GammaRamp.ChannelLength - 1]);
        Assert.Equal(ushort.MaxValue, GammaRamp.Identity.Values[(GammaRamp.ChannelLength * 2) - 1]);
        Assert.Equal(ushort.MaxValue, GammaRamp.Identity.Values[GammaRamp.ValueCount - 1]);
    }

    [Fact]
    public void Multipliers_AreNeutralAtLightBulbDaylightThreshold()
    {
        Assert.Equal(1, NightColorGammaRamp.RedMultiplier(6600), precision: 12);
        Assert.Equal(1, NightColorGammaRamp.GreenMultiplier(6600), precision: 12);
        Assert.Equal(1, NightColorGammaRamp.BlueMultiplier(6600), precision: 12);
    }

    [Fact]
    public void Multipliers_ReduceBlueAndGreenForWarmNightTemperature()
    {
        var red = NightColorGammaRamp.RedMultiplier(3400);
        var green = NightColorGammaRamp.GreenMultiplier(3400);
        var blue = NightColorGammaRamp.BlueMultiplier(3400);

        Assert.Equal(1, red, precision: 12);
        Assert.InRange(green, 0.70, 0.90);
        Assert.InRange(blue, 0.40, 0.70);
        Assert.True(blue < green);
    }
}
