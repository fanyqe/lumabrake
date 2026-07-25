using LumaBrake.Domain.Analysis;

namespace LumaBrake.Domain.Tests;

public sealed class ProtectionTuningOptionsTests
{
    [Fact]
    public void Clamp_ReplacesInvalidChoicesWithDefaults()
    {
        var options = new ProtectionTuningOptions
        {
            Strength = (ProtectionStrengthPreset)99,
            Response = (ProtectionResponsePreset)99,
            LocalProtection = (LocalProtectionPreset)99,
            LocalMaskFit = (LocalMaskFitPreset)99,
            WhitePageProtection = (WhitePageProtectionPreset)99,
            StaticShadeProfile = (StaticShadePreset)99
        }.Clamp();

        Assert.Equal(ProtectionStrengthPreset.Balanced, options.Strength);
        Assert.Equal(ProtectionResponsePreset.Balanced, options.Response);
        Assert.Equal(LocalProtectionPreset.Balanced, options.LocalProtection);
        Assert.Equal(LocalMaskFitPreset.Precise, options.LocalMaskFit);
        Assert.Equal(WhitePageProtectionPreset.Normal, options.WhitePageProtection);
        Assert.Equal(StaticShadePreset.OledBalanced, options.StaticShadeProfile);
    }
}
