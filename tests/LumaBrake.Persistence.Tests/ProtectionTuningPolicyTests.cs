using LumaBrake.Domain.Analysis;
using LumaBrake.Persistence;

namespace LumaBrake.Persistence.Tests;

public sealed class ProtectionTuningPolicyTests
{
    [Theory]
    [InlineData(ProtectionPreset.Comfort, ProtectionStrengthPreset.Gentle, ProtectionResponsePreset.Calm, LocalProtectionPreset.Calm, LocalMaskFitPreset.Precise, WhitePageProtectionPreset.Light, StaticShadePreset.OfficeUse)]
    [InlineData(ProtectionPreset.Balanced, ProtectionStrengthPreset.Balanced, ProtectionResponsePreset.Balanced, LocalProtectionPreset.Balanced, LocalMaskFitPreset.Precise, WhitePageProtectionPreset.Normal, StaticShadePreset.OledBalanced)]
    [InlineData(ProtectionPreset.Strong, ProtectionStrengthPreset.Strong, ProtectionResponsePreset.Fast, LocalProtectionPreset.Strong, LocalMaskFitPreset.Balanced, WhitePageProtectionPreset.Strong, StaticShadePreset.StaticHud)]
    [InlineData(ProtectionPreset.Maximum, ProtectionStrengthPreset.Maximum, ProtectionResponsePreset.Fast, LocalProtectionPreset.Aggressive, LocalMaskFitPreset.Expanded, WhitePageProtectionPreset.Strong, StaticShadePreset.OledAggressive)]
    public void ApplyProtectionPreset_AppliesAndDetectsOneClickPreset(
        ProtectionPreset preset,
        ProtectionStrengthPreset strength,
        ProtectionResponsePreset response,
        LocalProtectionPreset localProtection,
        LocalMaskFitPreset localMaskFit,
        WhitePageProtectionPreset whitePageProtection,
        StaticShadePreset staticShade)
    {
        var tuned = ProtectionTuningPolicy.ApplyProtectionPreset(new LumaBrakeSettings
        {
            Enabled = false,
            SpikeGuard = new SpikeDetectorOptions { Enabled = false },
            FlashGuard = new FlashDetectorOptions { Enabled = false, RedFlashDetectionEnabled = false },
            StaticShade = new StaticShadeOptions { Enabled = false },
            AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
            InactiveMonitor = new InactiveMonitorOptions { Enabled = false }
        }, preset);

        Assert.True(tuned.Enabled);
        Assert.True(tuned.SpikeGuard.Enabled);
        Assert.True(tuned.FlashGuard.Enabled);
        Assert.True(tuned.FlashGuard.RedFlashDetectionEnabled);
        Assert.True(tuned.StaticShade.Enabled);
        Assert.True(tuned.AdaptiveContent.Enabled);
        Assert.True(tuned.InactiveMonitor.Enabled);
        Assert.Equal(strength, tuned.ProtectionTuning.Strength);
        Assert.Equal(response, tuned.ProtectionTuning.Response);
        Assert.Equal(localProtection, tuned.ProtectionTuning.LocalProtection);
        Assert.Equal(localMaskFit, tuned.ProtectionTuning.LocalMaskFit);
        Assert.Equal(whitePageProtection, tuned.ProtectionTuning.WhitePageProtection);
        Assert.Equal(staticShade, tuned.ProtectionTuning.StaticShadeProfile);
        Assert.Equal(preset, ProtectionTuningPolicy.DetectProtectionPreset(tuned));
    }

    [Fact]
    public void DetectProtectionPreset_ReturnsCustomWhenDetailedTuningChanges()
    {
        var custom = ProtectionTuningPolicy.ApplyProtectionPreset(new LumaBrakeSettings(), ProtectionPreset.Balanced) with
        {
            ProtectionTuning = new ProtectionTuningOptions
            {
                LocalProtection = LocalProtectionPreset.Aggressive
            }
        };

        Assert.Equal(ProtectionPreset.Custom, ProtectionTuningPolicy.DetectProtectionPreset(custom));
    }

    [Fact]
    public void ApplyOledProtectionPreset_EnablesStrongOledProtectionDefaults()
    {
        var tuned = ProtectionTuningPolicy.ApplyOledProtectionPreset(new LumaBrakeSettings
        {
            Enabled = false,
            SpikeGuard = new SpikeDetectorOptions { Enabled = false },
            FlashGuard = new FlashDetectorOptions { Enabled = false, RedFlashDetectionEnabled = false },
            StaticShade = new StaticShadeOptions { Enabled = false },
            AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
            InactiveMonitor = new InactiveMonitorOptions
            {
                Enabled = false,
                MinimumInactiveDuration = TimeSpan.FromMinutes(5),
                Opacity = 0.12
            }
        });

        Assert.True(tuned.Enabled);
        Assert.True(tuned.SpikeGuard.Enabled);
        Assert.True(tuned.FlashGuard.Enabled);
        Assert.True(tuned.FlashGuard.RedFlashDetectionEnabled);
        Assert.True(tuned.StaticShade.Enabled);
        Assert.True(tuned.AdaptiveContent.Enabled);
        Assert.True(tuned.InactiveMonitor.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(2), tuned.InactiveMonitor.MinimumInactiveDuration);
        Assert.Equal(0.30, tuned.InactiveMonitor.Opacity, precision: 12);
        Assert.Equal(ProtectionStrengthPreset.Strong, tuned.ProtectionTuning.Strength);
        Assert.Equal(ProtectionResponsePreset.Fast, tuned.ProtectionTuning.Response);
        Assert.Equal(LocalProtectionPreset.Strong, tuned.ProtectionTuning.LocalProtection);
        Assert.Equal(LocalMaskFitPreset.Balanced, tuned.ProtectionTuning.LocalMaskFit);
        Assert.Equal(WhitePageProtectionPreset.Strong, tuned.ProtectionTuning.WhitePageProtection);
        Assert.Equal(StaticShadePreset.OledAggressive, tuned.ProtectionTuning.StaticShadeProfile);
        Assert.Equal(0.78, tuned.UserMaximumOpacity, precision: 12);
        Assert.Equal(0.68, tuned.SpikeGuard.MaximumOpacity, precision: 12);
        Assert.Equal(0.72, tuned.FlashGuard.MaximumOpacity, precision: 12);
        Assert.Equal(0.50, tuned.StaticShade.MaximumOpacity, precision: 12);
        Assert.Equal(0.36, tuned.AdaptiveContent.MaximumOpacity, precision: 12);
        Assert.True(ProtectionTuningPolicy.IsOledProtectionPreset(tuned));
        Assert.Equal(ProtectionPreset.OledCare, ProtectionTuningPolicy.DetectProtectionPreset(tuned));
    }

    [Fact]
    public void ApplyBalancedProtectionPreset_ReturnsToGeneralBalancedProtection()
    {
        var balanced = ProtectionTuningPolicy.ApplyBalancedProtectionPreset(
            ProtectionTuningPolicy.ApplyOledProtectionPreset(new LumaBrakeSettings()));

        Assert.Equal(ProtectionStrengthPreset.Balanced, balanced.ProtectionTuning.Strength);
        Assert.Equal(ProtectionResponsePreset.Balanced, balanced.ProtectionTuning.Response);
        Assert.Equal(LocalProtectionPreset.Balanced, balanced.ProtectionTuning.LocalProtection);
        Assert.Equal(LocalMaskFitPreset.Precise, balanced.ProtectionTuning.LocalMaskFit);
        Assert.Equal(WhitePageProtectionPreset.Normal, balanced.ProtectionTuning.WhitePageProtection);
        Assert.Equal(StaticShadePreset.OledBalanced, balanced.ProtectionTuning.StaticShadeProfile);
        Assert.True(balanced.SpikeGuard.Enabled);
        Assert.True(balanced.FlashGuard.Enabled);
        Assert.True(balanced.StaticShade.Enabled);
        Assert.True(balanced.AdaptiveContent.Enabled);
        Assert.True(balanced.InactiveMonitor.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(5), balanced.InactiveMonitor.MinimumInactiveDuration);
        Assert.Equal(0.22, balanced.InactiveMonitor.Opacity, precision: 12);
        Assert.Equal(TimeSpan.FromMilliseconds(650), balanced.InactiveMonitor.Release);
        Assert.False(ProtectionTuningPolicy.IsOledProtectionPreset(balanced));
    }

    [Fact]
    public void Apply_StrongFastLocalTuningRaisesProtectionAndResponsiveness()
    {
        var tuned = ProtectionTuningPolicy.Apply(new LumaBrakeSettings
        {
            ProtectionTuning = new ProtectionTuningOptions
            {
                Strength = ProtectionStrengthPreset.Strong,
                Response = ProtectionResponsePreset.Fast,
                LocalProtection = LocalProtectionPreset.Strong,
                LocalMaskFit = LocalMaskFitPreset.Balanced,
                WhitePageProtection = WhitePageProtectionPreset.Strong,
                StaticShadeProfile = StaticShadePreset.StaticHud
            }
        });

        Assert.Equal(0.78, tuned.UserMaximumOpacity, precision: 12);
        Assert.Equal(0.68, tuned.SpikeGuard.MaximumOpacity, precision: 12);
        Assert.Equal(0.72, tuned.FlashGuard.MaximumOpacity, precision: 12);
        Assert.Equal(0.50, tuned.StaticShade.MaximumOpacity, precision: 12);
        Assert.Equal(0.36, tuned.AdaptiveContent.MaximumOpacity, precision: 12);
        Assert.True(tuned.SpikeGuard.LocalizedSpikeDetectionEnabled);
        Assert.Equal(LocalMaskFitPreset.Balanced, tuned.SpikeGuard.LocalizedMaskFit);
        Assert.Equal(LocalMaskFitPreset.Balanced, tuned.FlashGuard.RegionalMaskFit);
        Assert.Equal(LocalMaskFitPreset.Balanced, tuned.StaticShade.RegionalMaskFit);
        Assert.Equal(0.010, tuned.FlashGuard.RegionalSignalPadding, precision: 12);
        Assert.Equal(TimeSpan.FromMilliseconds(150), tuned.StaticShade.ReacquireMinimumStaticDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(260), tuned.AdaptiveContent.MinimumBrightDuration);
        Assert.Equal(StaticShadePreset.StaticHud, tuned.ProtectionTuning.StaticShadeProfile);
    }

    [Fact]
    public void Apply_GentleCalmTuningKeepsMasksSofterAndSlower()
    {
        var tuned = ProtectionTuningPolicy.Apply(new LumaBrakeSettings
        {
            ProtectionTuning = new ProtectionTuningOptions
            {
                Strength = ProtectionStrengthPreset.Gentle,
                Response = ProtectionResponsePreset.Calm,
                LocalProtection = LocalProtectionPreset.Calm,
                LocalMaskFit = LocalMaskFitPreset.Precise,
                WhitePageProtection = WhitePageProtectionPreset.Light,
                StaticShadeProfile = StaticShadePreset.OfficeUse
            }
        });

        Assert.Equal(0.46, tuned.UserMaximumOpacity, precision: 12);
        Assert.Equal(0.44, tuned.SpikeGuard.MaximumOpacity, precision: 12);
        Assert.Equal(0.48, tuned.FlashGuard.MaximumOpacity, precision: 12);
        Assert.Equal(0.26, tuned.StaticShade.MaximumOpacity, precision: 12);
        Assert.Equal(0.18, tuned.AdaptiveContent.MaximumOpacity, precision: 12);
        Assert.False(tuned.SpikeGuard.LocalizedSpikeDetectionEnabled);
        Assert.Equal(LocalMaskFitPreset.Precise, tuned.StaticShade.RegionalMaskFit);
        Assert.Equal(0.004, tuned.StaticShade.RegionalSignalPadding, precision: 12);
        Assert.Equal(TimeSpan.FromMilliseconds(340), tuned.StaticShade.ReacquireMinimumStaticDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(700), tuned.AdaptiveContent.MinimumBrightDuration);
        Assert.Equal(StaticShadePreset.OfficeUse, tuned.ProtectionTuning.StaticShadeProfile);
    }

    [Fact]
    public void Apply_PreservesModuleEnabledFlags()
    {
        var tuned = ProtectionTuningPolicy.Apply(new LumaBrakeSettings
        {
            SpikeGuard = new SpikeDetectorOptions { Enabled = false },
            FlashGuard = new FlashDetectorOptions { Enabled = false, RedFlashDetectionEnabled = false },
            StaticShade = new StaticShadeOptions { Enabled = false },
            AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
            ProtectionTuning = new ProtectionTuningOptions
            {
                Strength = ProtectionStrengthPreset.Maximum,
                Response = ProtectionResponsePreset.Fast,
                LocalProtection = LocalProtectionPreset.Aggressive,
                LocalMaskFit = LocalMaskFitPreset.Expanded,
                WhitePageProtection = WhitePageProtectionPreset.Strong,
                StaticShadeProfile = StaticShadePreset.OledAggressive
            }
        });

        Assert.False(tuned.SpikeGuard.Enabled);
        Assert.False(tuned.FlashGuard.Enabled);
        Assert.False(tuned.FlashGuard.RedFlashDetectionEnabled);
        Assert.False(tuned.StaticShade.Enabled);
        Assert.False(tuned.AdaptiveContent.Enabled);
        Assert.True(tuned.SpikeGuard.LocalizedSpikeDetectionEnabled);
        Assert.Equal(LocalMaskFitPreset.Expanded, tuned.FlashGuard.RegionalMaskFit);
        Assert.Equal(0.88, tuned.UserMaximumOpacity, precision: 12);
    }
}
