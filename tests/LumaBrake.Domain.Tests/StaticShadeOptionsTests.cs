using LumaBrake.Domain.Analysis;

namespace LumaBrake.Domain.Tests;

public sealed class StaticShadeOptionsTests
{
    [Fact]
    public void ForPreset_DisabledTurnsOffStaticShade()
    {
        var options = StaticShadeOptions.ForPreset(StaticShadePreset.Disabled);

        Assert.False(options.Enabled);
    }

    [Fact]
    public void ForPreset_OledAggressiveRespondsEarlierThanBalanced()
    {
        var balanced = StaticShadeOptions.ForPreset(StaticShadePreset.OledBalanced);
        var aggressive = StaticShadeOptions.ForPreset(StaticShadePreset.OledAggressive);

        Assert.True(aggressive.MinimumStaticDuration < balanced.MinimumStaticDuration);
        Assert.True(aggressive.MinimumExposureScore < balanced.MinimumExposureScore);
        Assert.True(aggressive.MaximumOpacity > balanced.MaximumOpacity);
    }

    [Fact]
    public void ForPreset_OfficeUseKeepsGentlerMaximumOpacity()
    {
        var balanced = StaticShadeOptions.ForPreset(StaticShadePreset.OledBalanced);
        var office = StaticShadeOptions.ForPreset(StaticShadePreset.OfficeUse);

        Assert.True(office.MinimumStaticDuration > balanced.MinimumStaticDuration);
        Assert.True(office.MaximumOpacity < balanced.MaximumOpacity);
    }
}
