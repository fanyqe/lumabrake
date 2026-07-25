using LumaBrake.Domain.Analysis;

namespace LumaBrake.Persistence;

public enum ProtectionPreset
{
    Comfort,
    Balanced,
    Strong,
    Maximum,
    OledCare,
    Custom
}

public static class ProtectionTuningPolicy
{
    private static readonly ProtectionPreset[] DetectableProtectionPresets =
    [
        ProtectionPreset.Comfort,
        ProtectionPreset.Balanced,
        ProtectionPreset.Strong,
        ProtectionPreset.Maximum,
        ProtectionPreset.OledCare
    ];

    public static LumaBrakeSettings ApplyProtectionPreset(LumaBrakeSettings settings, ProtectionPreset preset)
    {
        if (preset == ProtectionPreset.Custom)
        {
            return Apply(settings);
        }

        if (preset == ProtectionPreset.OledCare)
        {
            return ApplyOledProtectionPreset(settings);
        }

        var tuning = ProtectionTuningForPreset(preset);
        var updated = settings with
        {
            Enabled = true,
            ProtectionTuning = tuning,
            SpikeGuard = settings.SpikeGuard with { Enabled = true },
            FlashGuard = settings.FlashGuard with
            {
                Enabled = true,
                RedFlashDetectionEnabled = true
            },
            StaticShade = settings.StaticShade with { Enabled = tuning.StaticShadeProfile != StaticShadePreset.Disabled },
            AdaptiveContent = settings.AdaptiveContent with { Enabled = true },
            InactiveMonitor = InactiveMonitorForPreset(preset)
        };

        return Apply(updated);
    }

    public static ProtectionPreset DetectProtectionPreset(LumaBrakeSettings settings)
    {
        foreach (var preset in DetectableProtectionPresets)
        {
            if (MatchesProtectionPreset(settings, preset))
            {
                return preset;
            }
        }

        return ProtectionPreset.Custom;
    }

    public static LumaBrakeSettings ApplyOledProtectionPreset(LumaBrakeSettings settings)
    {
        var updated = settings with
        {
            Enabled = true,
            ProtectionTuning = OledProtectionTuning(),
            SpikeGuard = settings.SpikeGuard with { Enabled = true },
            FlashGuard = settings.FlashGuard with
            {
                Enabled = true,
                RedFlashDetectionEnabled = true
            },
            StaticShade = settings.StaticShade with { Enabled = true },
            AdaptiveContent = settings.AdaptiveContent with { Enabled = true },
            InactiveMonitor = settings.InactiveMonitor with
            {
                Enabled = true,
                MinimumInactiveDuration = TimeSpan.FromMinutes(2),
                Opacity = Math.Max(settings.InactiveMonitor.Opacity, 0.30),
                Release = TimeSpan.FromMilliseconds(480)
            }
        };

        return Apply(updated);
    }

    public static LumaBrakeSettings ApplyBalancedProtectionPreset(LumaBrakeSettings settings)
    {
        var updated = settings with
        {
            ProtectionTuning = new ProtectionTuningOptions(),
            SpikeGuard = settings.SpikeGuard with { Enabled = true },
            FlashGuard = settings.FlashGuard with
            {
                Enabled = true,
                RedFlashDetectionEnabled = true
            },
            StaticShade = settings.StaticShade with { Enabled = true },
            AdaptiveContent = settings.AdaptiveContent with { Enabled = true },
            InactiveMonitor = new InactiveMonitorOptions
            {
                Enabled = settings.InactiveMonitor.Enabled
            }
        };

        return Apply(updated);
    }

    public static bool IsOledProtectionPreset(LumaBrakeSettings settings)
    {
        var tuning = settings.ProtectionTuning.Clamp();
        var oledTuning = OledProtectionTuning();

        return settings.Enabled &&
            settings.SpikeGuard.Enabled &&
            settings.FlashGuard.Enabled &&
            settings.FlashGuard.RedFlashDetectionEnabled &&
            settings.StaticShade.Enabled &&
            settings.AdaptiveContent.Enabled &&
            settings.InactiveMonitor.Enabled &&
            settings.InactiveMonitor.MinimumInactiveDuration <= TimeSpan.FromMinutes(2) &&
            settings.InactiveMonitor.Opacity >= 0.30 &&
            settings.InactiveMonitor.Release <= TimeSpan.FromMilliseconds(480) &&
            tuning.Strength == oledTuning.Strength &&
            tuning.Response == oledTuning.Response &&
            tuning.LocalProtection == oledTuning.LocalProtection &&
            tuning.LocalMaskFit == oledTuning.LocalMaskFit &&
            tuning.WhitePageProtection == oledTuning.WhitePageProtection &&
            tuning.StaticShadeProfile == oledTuning.StaticShadeProfile;
    }

    public static LumaBrakeSettings Apply(LumaBrakeSettings settings)
    {
        var tuning = settings.ProtectionTuning.Clamp();
        var spikeEnabled = settings.SpikeGuard.Enabled;
        var flashEnabled = settings.FlashGuard.Enabled;
        var redFlashEnabled = settings.FlashGuard.RedFlashDetectionEnabled;
        var adaptiveEnabled = settings.AdaptiveContent.Enabled;
        var staticShadeEnabled = settings.StaticShade.Enabled && tuning.StaticShadeProfile != StaticShadePreset.Disabled;
        var staticShadeBase = StaticShadeOptions.ForPreset(tuning.StaticShadeProfile);

        return settings with
        {
            ProtectionTuning = tuning,
            UserMaximumOpacity = UserMaximumOpacity(tuning.Strength),
            SpikeGuard = TuneSpike(settings.SpikeGuard, tuning) with
            {
                Enabled = spikeEnabled
            },
            FlashGuard = TuneFlash(settings.FlashGuard, tuning) with
            {
                Enabled = flashEnabled,
                RedFlashDetectionEnabled = redFlashEnabled
            },
            StaticShade = TuneStaticShade(staticShadeBase, tuning) with
            {
                Enabled = staticShadeEnabled
            },
            AdaptiveContent = TuneAdaptive(settings.AdaptiveContent, tuning) with
            {
                Enabled = adaptiveEnabled
            }
        };
    }

    private static SpikeDetectorOptions TuneSpike(SpikeDetectorOptions options, ProtectionTuningOptions tuning)
    {
        var tuned = tuning.Strength switch
        {
            ProtectionStrengthPreset.Gentle => options with
            {
                MaximumOpacity = 0.44,
                MinimumActivationLuminance = 0.32,
                AbsoluteIncreaseThreshold = 0.22,
                BrightPixelRatioThreshold = 0.22
            },
            ProtectionStrengthPreset.Strong => options with
            {
                MaximumOpacity = 0.68,
                MinimumActivationLuminance = 0.24,
                AbsoluteIncreaseThreshold = 0.15,
                BrightPixelRatioThreshold = 0.14
            },
            ProtectionStrengthPreset.Maximum => options with
            {
                MaximumOpacity = 0.78,
                MinimumActivationLuminance = 0.20,
                AbsoluteIncreaseThreshold = 0.12,
                BrightPixelRatioThreshold = 0.10
            },
            _ => options with
            {
                MaximumOpacity = 0.58,
                MinimumActivationLuminance = 0.28,
                AbsoluteIncreaseThreshold = 0.18,
                BrightPixelRatioThreshold = 0.18
            }
        };

        tuned = tuning.Response switch
        {
            ProtectionResponsePreset.Calm => tuned with
            {
                Cooldown = TimeSpan.FromMilliseconds(1100),
                Hold = TimeSpan.FromMilliseconds(280),
                Release = TimeSpan.FromMilliseconds(1000)
            },
            ProtectionResponsePreset.Fast => tuned with
            {
                Cooldown = TimeSpan.FromMilliseconds(620),
                Hold = TimeSpan.FromMilliseconds(150),
                Release = TimeSpan.FromMilliseconds(560)
            },
            _ => tuned with
            {
                Cooldown = TimeSpan.FromMilliseconds(900),
                Hold = TimeSpan.FromMilliseconds(220),
                Release = TimeSpan.FromMilliseconds(900)
            }
        };

        tuned = tuning.LocalProtection switch
        {
            LocalProtectionPreset.Calm => tuned with
            {
                LocalizedSpikeDetectionEnabled = false,
                MinimumLocalizedChangedTileRatio = 0.006,
                MaximumLocalizedChangedTileRatio = 0.22
            },
            LocalProtectionPreset.Strong => tuned with
            {
                LocalizedSpikeDetectionEnabled = true,
                MinimumLocalizedChangedTileRatio = 0.001,
                MaximumLocalizedChangedTileRatio = 0.42
            },
            LocalProtectionPreset.Aggressive => tuned with
            {
                LocalizedSpikeDetectionEnabled = true,
                MinimumLocalizedChangedTileRatio = 0.0008,
                MaximumLocalizedChangedTileRatio = 0.52
            },
            _ => tuned with
            {
                LocalizedSpikeDetectionEnabled = false,
                MinimumLocalizedChangedTileRatio = 0.0015,
                MaximumLocalizedChangedTileRatio = 0.35
            }
        };

        return ApplyMaskFit(tuned, tuning.LocalMaskFit);
    }

    private static FlashDetectorOptions TuneFlash(FlashDetectorOptions options, ProtectionTuningOptions tuning)
    {
        var tuned = tuning.Strength switch
        {
            ProtectionStrengthPreset.Gentle => options with { MaximumOpacity = 0.48 },
            ProtectionStrengthPreset.Strong => options with { MaximumOpacity = 0.72 },
            ProtectionStrengthPreset.Maximum => options with { MaximumOpacity = 0.82 },
            _ => options with { MaximumOpacity = 0.62 }
        };

        tuned = tuning.Response switch
        {
            ProtectionResponsePreset.Calm => tuned with
            {
                Cooldown = TimeSpan.FromMilliseconds(720),
                Hold = TimeSpan.FromMilliseconds(380),
                Release = TimeSpan.FromMilliseconds(1050)
            },
            ProtectionResponsePreset.Fast => tuned with
            {
                Cooldown = TimeSpan.FromMilliseconds(360),
                Hold = TimeSpan.FromMilliseconds(210),
                Release = TimeSpan.FromMilliseconds(640)
            },
            _ => tuned with
            {
                Cooldown = TimeSpan.FromMilliseconds(500),
                Hold = TimeSpan.FromMilliseconds(320),
                Release = TimeSpan.FromMilliseconds(950)
            }
        };

        tuned = tuning.LocalProtection switch
        {
            LocalProtectionPreset.Calm => tuned with
            {
                MinimumAffectedTileRatio = 0.006,
                EarlyLocalizedMaximumAffectedTileRatio = 0.20
            },
            LocalProtectionPreset.Strong => tuned with
            {
                MinimumAffectedTileRatio = 0.001,
                EarlyLocalizedMaximumAffectedTileRatio = 0.42
            },
            LocalProtectionPreset.Aggressive => tuned with
            {
                MinimumAffectedTileRatio = 0.0008,
                EarlyLocalizedMaximumAffectedTileRatio = 0.52
            },
            _ => tuned with
            {
                MinimumAffectedTileRatio = 0.0015,
                EarlyLocalizedMaximumAffectedTileRatio = 0.35
            }
        };

        return ApplyMaskFit(tuned, tuning.LocalMaskFit);
    }

    private static StaticShadeOptions TuneStaticShade(StaticShadeOptions options, ProtectionTuningOptions tuning)
    {
        var tuned = tuning.Strength switch
        {
            ProtectionStrengthPreset.Gentle => options with
            {
                BaseOpacity = 0.08,
                MaximumOpacity = 0.26,
                MinimumExposureScore = Math.Max(options.MinimumExposureScore, 0.20)
            },
            ProtectionStrengthPreset.Strong => options with
            {
                BaseOpacity = 0.16,
                MaximumOpacity = Math.Max(options.MaximumOpacity, 0.50),
                MinimumExposureScore = Math.Min(options.MinimumExposureScore, 0.14)
            },
            ProtectionStrengthPreset.Maximum => options with
            {
                BaseOpacity = 0.20,
                MaximumOpacity = Math.Max(options.MaximumOpacity, 0.62),
                MinimumExposureScore = Math.Min(options.MinimumExposureScore, 0.10)
            },
            _ => options
        };

        tuned = tuning.Response switch
        {
            ProtectionResponsePreset.Calm => tuned with
            {
                Hold = TimeSpan.FromMilliseconds(180),
                Release = TimeSpan.FromMilliseconds(420),
                ReacquireWindow = TimeSpan.FromMilliseconds(1200),
                ReacquireMinimumStaticDuration = TimeSpan.FromMilliseconds(340)
            },
            ProtectionResponsePreset.Fast => tuned with
            {
                MinimumStaticDuration = ShorterStaticDuration(tuned.MinimumStaticDuration),
                Hold = TimeSpan.FromMilliseconds(80),
                Release = TimeSpan.FromMilliseconds(180),
                ReacquireWindow = TimeSpan.FromMilliseconds(1800),
                ReacquireMinimumStaticDuration = TimeSpan.FromMilliseconds(150)
            },
            _ => tuned
        };

        tuned = tuning.LocalProtection switch
        {
            LocalProtectionPreset.Calm => tuned with
            {
                MinimumAffectedTileRatio = 0.010,
                MaximumAffectedTileRatio = Math.Min(tuned.MaximumAffectedTileRatio, 0.32)
            },
            LocalProtectionPreset.Strong => tuned with
            {
                MinimumAffectedTileRatio = 0.002,
                MaximumAffectedTileRatio = Math.Max(tuned.MaximumAffectedTileRatio, 0.55)
            },
            LocalProtectionPreset.Aggressive => tuned with
            {
                MinimumAffectedTileRatio = 0.001,
                MaximumAffectedTileRatio = Math.Max(tuned.MaximumAffectedTileRatio, 0.65),
                BrightTileAverageThreshold = Math.Min(tuned.BrightTileAverageThreshold, 0.54),
                BrightTilePercentile95Threshold = Math.Min(tuned.BrightTilePercentile95Threshold, 0.62),
                BrightTilePixelRatioThreshold = Math.Min(tuned.BrightTilePixelRatioThreshold, 0.24)
            },
            _ => tuned
        };

        return ApplyMaskFit(tuned, tuning.LocalMaskFit);
    }

    private static SpikeDetectorOptions ApplyMaskFit(SpikeDetectorOptions options, LocalMaskFitPreset fit) =>
        fit switch
        {
            LocalMaskFitPreset.Balanced => options with
            {
                LocalizedMaskFit = LocalMaskFitPreset.Balanced,
                LocalizedSignalPadding = 0.010,
                SuppressLocalizedBrightSceneScroll = true
            },
            LocalMaskFitPreset.Expanded => options with
            {
                LocalizedMaskFit = LocalMaskFitPreset.Expanded,
                LocalizedSignalPadding = 0.020,
                SuppressLocalizedBrightSceneScroll = true
            },
            _ => options with
            {
                LocalizedMaskFit = LocalMaskFitPreset.Precise,
                LocalizedSignalPadding = 0.004,
                SuppressLocalizedBrightSceneScroll = true
            }
        };

    private static FlashDetectorOptions ApplyMaskFit(FlashDetectorOptions options, LocalMaskFitPreset fit) =>
        fit switch
        {
            LocalMaskFitPreset.Balanced => options with
            {
                RegionalMaskFit = LocalMaskFitPreset.Balanced,
                RegionalSignalPadding = 0.010,
                SuppressLocalizedBrightSceneScroll = true
            },
            LocalMaskFitPreset.Expanded => options with
            {
                RegionalMaskFit = LocalMaskFitPreset.Expanded,
                RegionalSignalPadding = 0.020,
                SuppressLocalizedBrightSceneScroll = true
            },
            _ => options with
            {
                RegionalMaskFit = LocalMaskFitPreset.Precise,
                RegionalSignalPadding = 0.004,
                SuppressLocalizedBrightSceneScroll = true
            }
        };

    private static StaticShadeOptions ApplyMaskFit(StaticShadeOptions options, LocalMaskFitPreset fit) =>
        fit switch
        {
            LocalMaskFitPreset.Balanced => options with
            {
                RegionalMaskFit = LocalMaskFitPreset.Balanced,
                RegionalSignalPadding = 0.010,
                SignalSurfaceMinimumCoverageRatio = 0.14,
                SignalSurfaceMinimumPercentile95Luminance = 0.56
            },
            LocalMaskFitPreset.Expanded => options with
            {
                RegionalMaskFit = LocalMaskFitPreset.Expanded,
                RegionalSignalPadding = 0.020,
                SignalSurfaceMinimumCoverageRatio = 0.18,
                SignalSurfaceMinimumPercentile95Luminance = 0.58
            },
            _ => options with
            {
                RegionalMaskFit = LocalMaskFitPreset.Precise,
                RegionalSignalPadding = 0.004,
                SignalSurfaceMinimumCoverageRatio = 0.08,
                SignalSurfaceMinimumPercentile95Luminance = 0.54
            }
        };

    private static AdaptiveContentOptions TuneAdaptive(AdaptiveContentOptions options, ProtectionTuningOptions tuning)
    {
        var tuned = tuning.Strength switch
        {
            ProtectionStrengthPreset.Gentle => options with
            {
                MinimumOpacity = 0.04,
                MaximumOpacity = 0.18
            },
            ProtectionStrengthPreset.Strong => options with
            {
                MinimumOpacity = 0.08,
                MaximumOpacity = 0.36
            },
            ProtectionStrengthPreset.Maximum => options with
            {
                MinimumOpacity = 0.10,
                MaximumOpacity = 0.46
            },
            _ => options with
            {
                MinimumOpacity = 0.05,
                MaximumOpacity = 0.24
            }
        };

        tuned = tuning.Response switch
        {
            ProtectionResponsePreset.Calm => tuned with
            {
                MinimumBrightDuration = TimeSpan.FromMilliseconds(700),
                SmoothingFactor = 0.25,
                Hold = TimeSpan.FromMilliseconds(420),
                Release = TimeSpan.FromMilliseconds(1100)
            },
            ProtectionResponsePreset.Fast => tuned with
            {
                MinimumBrightDuration = TimeSpan.FromMilliseconds(260),
                SmoothingFactor = 0.55,
                Hold = TimeSpan.FromMilliseconds(170),
                Release = TimeSpan.FromMilliseconds(560)
            },
            _ => tuned with
            {
                MinimumBrightDuration = TimeSpan.FromMilliseconds(450),
                SmoothingFactor = 0.35,
                Hold = TimeSpan.FromMilliseconds(300),
                Release = TimeSpan.FromMilliseconds(900)
            }
        };

        return tuning.WhitePageProtection switch
        {
            WhitePageProtectionPreset.Light => tuned with
            {
                MinimumAverageLuminance = 0.50,
                MinimumPercentile95Luminance = 0.78,
                MinimumBrightPixelRatio = 0.44,
                MaximumOpacity = Math.Min(tuned.MaximumOpacity, 0.22)
            },
            WhitePageProtectionPreset.Strong => tuned with
            {
                MinimumAverageLuminance = 0.36,
                MinimumPercentile95Luminance = 0.62,
                MinimumBrightPixelRatio = 0.26,
                MaximumOpacity = Math.Max(tuned.MaximumOpacity, 0.36)
            },
            _ => tuned with
            {
                MinimumAverageLuminance = 0.42,
                MinimumPercentile95Luminance = 0.70,
                MinimumBrightPixelRatio = 0.35
            }
        };
    }

    private static double UserMaximumOpacity(ProtectionStrengthPreset strength) =>
        strength switch
        {
            ProtectionStrengthPreset.Gentle => 0.46,
            ProtectionStrengthPreset.Strong => 0.78,
            ProtectionStrengthPreset.Maximum => 0.88,
            _ => 0.64
        };

    private static bool MatchesProtectionPreset(LumaBrakeSettings settings, ProtectionPreset preset)
    {
        if (preset == ProtectionPreset.OledCare)
        {
            return IsOledProtectionPreset(settings);
        }

        var tuning = settings.ProtectionTuning.Clamp();
        var presetTuning = ProtectionTuningForPreset(preset);
        var inactiveMonitor = InactiveMonitorForPreset(preset);

        return settings.SpikeGuard.Enabled &&
            settings.FlashGuard.Enabled &&
            settings.FlashGuard.RedFlashDetectionEnabled &&
            settings.StaticShade.Enabled == (presetTuning.StaticShadeProfile != StaticShadePreset.Disabled) &&
            settings.AdaptiveContent.Enabled &&
            settings.InactiveMonitor == inactiveMonitor &&
            tuning.Strength == presetTuning.Strength &&
            tuning.Response == presetTuning.Response &&
            tuning.LocalProtection == presetTuning.LocalProtection &&
            tuning.LocalMaskFit == presetTuning.LocalMaskFit &&
            tuning.WhitePageProtection == presetTuning.WhitePageProtection &&
            tuning.StaticShadeProfile == presetTuning.StaticShadeProfile;
    }

    private static ProtectionTuningOptions ProtectionTuningForPreset(ProtectionPreset preset) =>
        preset switch
        {
            ProtectionPreset.Comfort => new ProtectionTuningOptions
            {
                Strength = ProtectionStrengthPreset.Gentle,
                Response = ProtectionResponsePreset.Calm,
                LocalProtection = LocalProtectionPreset.Calm,
                LocalMaskFit = LocalMaskFitPreset.Precise,
                WhitePageProtection = WhitePageProtectionPreset.Light,
                StaticShadeProfile = StaticShadePreset.OfficeUse
            },
            ProtectionPreset.Strong => new ProtectionTuningOptions
            {
                Strength = ProtectionStrengthPreset.Strong,
                Response = ProtectionResponsePreset.Fast,
                LocalProtection = LocalProtectionPreset.Strong,
                LocalMaskFit = LocalMaskFitPreset.Balanced,
                WhitePageProtection = WhitePageProtectionPreset.Strong,
                StaticShadeProfile = StaticShadePreset.StaticHud
            },
            ProtectionPreset.Maximum => new ProtectionTuningOptions
            {
                Strength = ProtectionStrengthPreset.Maximum,
                Response = ProtectionResponsePreset.Fast,
                LocalProtection = LocalProtectionPreset.Aggressive,
                LocalMaskFit = LocalMaskFitPreset.Expanded,
                WhitePageProtection = WhitePageProtectionPreset.Strong,
                StaticShadeProfile = StaticShadePreset.OledAggressive
            },
            _ => new ProtectionTuningOptions()
        };

    private static InactiveMonitorOptions InactiveMonitorForPreset(ProtectionPreset preset) =>
        preset switch
        {
            ProtectionPreset.Comfort => new InactiveMonitorOptions
            {
                Enabled = true,
                MinimumInactiveDuration = TimeSpan.FromMinutes(8),
                Opacity = 0.16,
                Release = TimeSpan.FromMilliseconds(900)
            },
            ProtectionPreset.Strong => new InactiveMonitorOptions
            {
                Enabled = true,
                MinimumInactiveDuration = TimeSpan.FromMinutes(2),
                Opacity = 0.30,
                Release = TimeSpan.FromMilliseconds(480)
            },
            ProtectionPreset.Maximum => new InactiveMonitorOptions
            {
                Enabled = true,
                MinimumInactiveDuration = TimeSpan.FromMinutes(1),
                Opacity = 0.40,
                Release = TimeSpan.FromMilliseconds(350)
            },
            _ => new InactiveMonitorOptions { Enabled = true }
        };

    private static ProtectionTuningOptions OledProtectionTuning() =>
        new()
        {
            Strength = ProtectionStrengthPreset.Strong,
            Response = ProtectionResponsePreset.Fast,
            LocalProtection = LocalProtectionPreset.Strong,
            LocalMaskFit = LocalMaskFitPreset.Balanced,
            WhitePageProtection = WhitePageProtectionPreset.Strong,
            StaticShadeProfile = StaticShadePreset.OledAggressive
        };

    private static TimeSpan ShorterStaticDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.FromSeconds(1))
        {
            return duration;
        }

        return duration <= TimeSpan.FromSeconds(3)
            ? TimeSpan.FromSeconds(1)
            : TimeSpan.FromSeconds(3);
    }
}
