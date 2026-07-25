using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Color;

namespace LumaBrake.Persistence;

public static class SettingsMigration
{
    public const int CurrentSchemaVersion = 10;

    public static LumaBrakeSettings Apply(LumaBrakeSettings settings)
    {
        var migrated = settings;
        if (settings.SchemaVersion < 3)
        {
            migrated = ApplyResponsiveProtectionDefaults(migrated);
        }

        if (settings.SchemaVersion < 4)
        {
            migrated = ApplyPreciseFlashProtectionDefaults(migrated);
        }

        if (settings.SchemaVersion < 5)
        {
            migrated = ApplyCalmLocalizedProtectionDefaults(migrated);
        }

        if (settings.SchemaVersion < 6)
        {
            migrated = ApplyReadableBrightSurfaceDefaults(migrated);
        }

        if (settings.SchemaVersion < 7)
        {
            migrated = ApplyFastStaticShadeReleaseDefaults(migrated);
        }

        if (settings.SchemaVersion < 8)
        {
            migrated = ApplyProtectionTuningDefaults(migrated);
        }

        if (settings.SchemaVersion < 9)
        {
            migrated = ApplyLocalMaskFitDefaults(migrated);
        }

        if (settings.SchemaVersion < 10)
        {
            migrated = ApplyNightColorDefaults(migrated);
        }

        return migrated with
        {
            SchemaVersion = CurrentSchemaVersion
        };
    }

    private static LumaBrakeSettings ApplyResponsiveProtectionDefaults(LumaBrakeSettings settings) =>
        settings with
        {
            SpikeGuard = settings.SpikeGuard with
            {
                WarmupSamples = settings.SpikeGuard.WarmupSamples == 8
                    ? 3
                    : settings.SpikeGuard.WarmupSamples
            },
            FlashGuard = settings.FlashGuard with
            {
                MinimumTransitions = settings.FlashGuard.MinimumTransitions == 4
                    ? 3
                    : settings.FlashGuard.MinimumTransitions,
                Cooldown = settings.FlashGuard.Cooldown == TimeSpan.FromMilliseconds(650)
                    ? TimeSpan.FromMilliseconds(500)
                    : settings.FlashGuard.Cooldown
            },
            AdaptiveContent = settings.AdaptiveContent with
            {
                MinimumBrightDuration = settings.AdaptiveContent.MinimumBrightDuration == TimeSpan.FromMilliseconds(900)
                    ? TimeSpan.FromMilliseconds(450)
                    : settings.AdaptiveContent.MinimumBrightDuration,
                SmoothingFactor = Math.Abs(settings.AdaptiveContent.SmoothingFactor - 0.20) < 0.000001
                    ? 0.35
                    : settings.AdaptiveContent.SmoothingFactor
            }
        };

    private static LumaBrakeSettings ApplyPreciseFlashProtectionDefaults(LumaBrakeSettings settings) =>
        settings with
        {
            FlashGuard = settings.FlashGuard with
            {
                MinimumAffectedTileRatio = Math.Abs(settings.FlashGuard.MinimumAffectedTileRatio - 0.03) < 0.000001
                    ? 0.0015
                    : settings.FlashGuard.MinimumAffectedTileRatio
            }
        };

    private static LumaBrakeSettings ApplyCalmLocalizedProtectionDefaults(LumaBrakeSettings settings) =>
        settings with
        {
            SpikeGuard = settings.SpikeGuard with
            {
                LocalizedSpikeDetectionEnabled = false
            },
            FlashGuard = settings.FlashGuard with
            {
                EarlyLocalizedTransitions = settings.FlashGuard.EarlyLocalizedTransitions <= 2
                    ? 3
                    : settings.FlashGuard.EarlyLocalizedTransitions
            }
        };

    private static LumaBrakeSettings ApplyReadableBrightSurfaceDefaults(LumaBrakeSettings settings)
    {
        var staticShade = settings.StaticShade;
        var minimumStaticDuration = staticShade.MinimumStaticDuration;
        var fullOpacityDuration = staticShade.FullOpacityDuration;

        if (staticShade.MinimumStaticDuration == TimeSpan.FromSeconds(30) &&
            staticShade.FullOpacityDuration == TimeSpan.FromMinutes(3))
        {
            minimumStaticDuration = TimeSpan.FromSeconds(2);
            fullOpacityDuration = TimeSpan.FromSeconds(24);
        }
        else if (staticShade.MinimumStaticDuration == TimeSpan.FromSeconds(12) &&
            staticShade.FullOpacityDuration == TimeSpan.FromSeconds(90) &&
            NearlyEqual(staticShade.MinimumExposureScore, 0.10) &&
            NearlyEqual(staticShade.MaximumOpacity, 0.44))
        {
            minimumStaticDuration = TimeSpan.FromSeconds(1);
            fullOpacityDuration = TimeSpan.FromSeconds(18);
        }
        else if (staticShade.MinimumStaticDuration == TimeSpan.FromSeconds(10) &&
            staticShade.FullOpacityDuration == TimeSpan.FromSeconds(75) &&
            NearlyEqual(staticShade.MinimumAffectedTileRatio, 0.002) &&
            NearlyEqual(staticShade.MaximumAffectedTileRatio, 0.30))
        {
            minimumStaticDuration = TimeSpan.FromSeconds(2);
            fullOpacityDuration = TimeSpan.FromSeconds(24);
        }

        return settings with
        {
            StaticShade = staticShade with
            {
                MinimumStaticDuration = minimumStaticDuration,
                FullOpacityDuration = fullOpacityDuration
            }
        };
    }

    private static LumaBrakeSettings ApplyFastStaticShadeReleaseDefaults(LumaBrakeSettings settings)
    {
        var staticShade = settings.StaticShade;
        var hold = staticShade.Hold;
        var release = staticShade.Release;

        if (staticShade.Hold == TimeSpan.FromMilliseconds(500) &&
            staticShade.Release == TimeSpan.FromMilliseconds(1200))
        {
            hold = TimeSpan.FromMilliseconds(120);
            release = TimeSpan.FromMilliseconds(260);
        }
        else if (staticShade.Hold == TimeSpan.FromMilliseconds(500) &&
            staticShade.Release == TimeSpan.FromMilliseconds(1400) &&
            NearlyEqual(staticShade.MaximumOpacity, 0.36))
        {
            hold = TimeSpan.FromMilliseconds(160);
            release = TimeSpan.FromMilliseconds(320);
        }
        else if (staticShade.Hold == TimeSpan.FromMilliseconds(600) &&
            staticShade.Release == TimeSpan.FromMilliseconds(1600) &&
            NearlyEqual(staticShade.MaximumOpacity, 0.44))
        {
            hold = TimeSpan.FromMilliseconds(90);
            release = TimeSpan.FromMilliseconds(220);
        }
        else if (staticShade.Hold == TimeSpan.FromMilliseconds(120) &&
            staticShade.Release == TimeSpan.FromMilliseconds(260) &&
            staticShade.MinimumStaticDuration == TimeSpan.FromSeconds(1) &&
            staticShade.FullOpacityDuration == TimeSpan.FromSeconds(18) &&
            NearlyEqual(staticShade.MinimumExposureScore, 0.10) &&
            NearlyEqual(staticShade.MaximumOpacity, 0.44))
        {
            hold = TimeSpan.FromMilliseconds(90);
            release = TimeSpan.FromMilliseconds(220);
        }

        return settings with
        {
            StaticShade = staticShade with
            {
                Hold = hold,
                Release = release
            }
        };
    }

    private static LumaBrakeSettings ApplyProtectionTuningDefaults(LumaBrakeSettings settings) =>
        settings with
        {
            ProtectionTuning = settings.ProtectionTuning.Clamp() with
            {
                Strength = InferStrength(settings),
                Response = InferResponse(settings),
                LocalProtection = InferLocalProtection(settings),
                WhitePageProtection = InferWhitePageProtection(settings),
                StaticShadeProfile = InferStaticShadeProfile(settings.StaticShade)
            }
        };

    private static LumaBrakeSettings ApplyLocalMaskFitDefaults(LumaBrakeSettings settings) =>
        settings with
        {
            ProtectionTuning = settings.ProtectionTuning.Clamp() with
            {
                LocalMaskFit = InferLocalMaskFit(settings)
            }
        };

    private static LumaBrakeSettings ApplyNightColorDefaults(LumaBrakeSettings settings) =>
        settings with
        {
            NightColor = new NightColorOptions { Enabled = false }
        };

    private static ProtectionStrengthPreset InferStrength(LumaBrakeSettings settings)
    {
        if (settings.UserMaximumOpacity >= 0.84 ||
            settings.StaticShade.MaximumOpacity >= 0.58 ||
            settings.AdaptiveContent.MaximumOpacity >= 0.42)
        {
            return ProtectionStrengthPreset.Maximum;
        }

        if (settings.UserMaximumOpacity >= 0.74 ||
            settings.StaticShade.MaximumOpacity >= 0.48 ||
            settings.AdaptiveContent.MaximumOpacity >= 0.34)
        {
            return ProtectionStrengthPreset.Strong;
        }

        return settings.UserMaximumOpacity <= 0.50 ||
            settings.StaticShade.MaximumOpacity <= 0.26 ||
            settings.AdaptiveContent.MaximumOpacity <= 0.20
            ? ProtectionStrengthPreset.Gentle
            : ProtectionStrengthPreset.Balanced;
    }

    private static ProtectionResponsePreset InferResponse(LumaBrakeSettings settings)
    {
        if (settings.StaticShade.Release <= TimeSpan.FromMilliseconds(220) ||
            settings.StaticShade.ReacquireMinimumStaticDuration <= TimeSpan.FromMilliseconds(180) ||
            settings.AdaptiveContent.MinimumBrightDuration <= TimeSpan.FromMilliseconds(300))
        {
            return ProtectionResponsePreset.Fast;
        }

        return settings.StaticShade.Release >= TimeSpan.FromMilliseconds(380) ||
            settings.AdaptiveContent.MinimumBrightDuration >= TimeSpan.FromMilliseconds(650)
            ? ProtectionResponsePreset.Calm
            : ProtectionResponsePreset.Balanced;
    }

    private static LocalProtectionPreset InferLocalProtection(LumaBrakeSettings settings)
    {
        if (settings.SpikeGuard.LocalizedSpikeDetectionEnabled &&
            (settings.SpikeGuard.MaximumLocalizedChangedTileRatio >= 0.50 ||
            settings.StaticShade.MinimumAffectedTileRatio <= 0.001))
        {
            return LocalProtectionPreset.Aggressive;
        }

        if (settings.SpikeGuard.LocalizedSpikeDetectionEnabled ||
            settings.StaticShade.MaximumAffectedTileRatio >= 0.55)
        {
            return LocalProtectionPreset.Strong;
        }

        return settings.FlashGuard.MinimumAffectedTileRatio >= 0.006 ||
            settings.StaticShade.MinimumAffectedTileRatio >= 0.010
            ? LocalProtectionPreset.Calm
            : LocalProtectionPreset.Balanced;
    }

    private static LocalMaskFitPreset InferLocalMaskFit(LumaBrakeSettings settings)
    {
        var tuning = settings.ProtectionTuning.Clamp();
        if (tuning.LocalMaskFit != LocalMaskFitPreset.Precise)
        {
            return tuning.LocalMaskFit;
        }

        if (settings.SpikeGuard.LocalizedMaskFit == LocalMaskFitPreset.Expanded ||
            settings.FlashGuard.RegionalMaskFit == LocalMaskFitPreset.Expanded ||
            settings.StaticShade.RegionalMaskFit == LocalMaskFitPreset.Expanded)
        {
            return LocalMaskFitPreset.Expanded;
        }

        return settings.SpikeGuard.LocalizedMaskFit == LocalMaskFitPreset.Balanced ||
            settings.FlashGuard.RegionalMaskFit == LocalMaskFitPreset.Balanced ||
            settings.StaticShade.RegionalMaskFit == LocalMaskFitPreset.Balanced
            ? LocalMaskFitPreset.Balanced
            : LocalMaskFitPreset.Precise;
    }

    private static WhitePageProtectionPreset InferWhitePageProtection(LumaBrakeSettings settings)
    {
        if (settings.AdaptiveContent.MaximumOpacity >= 0.34 ||
            settings.AdaptiveContent.MinimumAverageLuminance <= 0.38)
        {
            return WhitePageProtectionPreset.Strong;
        }

        return settings.AdaptiveContent.MaximumOpacity <= 0.22 ||
            settings.AdaptiveContent.MinimumAverageLuminance >= 0.48
            ? WhitePageProtectionPreset.Light
            : WhitePageProtectionPreset.Normal;
    }

    private static StaticShadePreset InferStaticShadeProfile(StaticShadeOptions staticShade)
    {
        if (!staticShade.Enabled)
        {
            return StaticShadePreset.Disabled;
        }

        if (staticShade.MinimumStaticDuration >= TimeSpan.FromSeconds(50) ||
            NearlyEqual(staticShade.MaximumOpacity, 0.24) ||
            staticShade.MinimumExposureScore >= 0.24)
        {
            return StaticShadePreset.OfficeUse;
        }

        if (staticShade.MinimumStaticDuration <= TimeSpan.FromSeconds(1) ||
            NearlyEqual(staticShade.MaximumOpacity, 0.44))
        {
            return StaticShadePreset.OledAggressive;
        }

        if (staticShade.MaximumAffectedTileRatio <= 0.30 ||
            NearlyEqual(staticShade.MinimumAffectedTileRatio, 0.002))
        {
            return StaticShadePreset.StaticHud;
        }

        if (staticShade.MinimumStaticDuration >= TimeSpan.FromSeconds(20) ||
            NearlyEqual(staticShade.MaximumOpacity, 0.36))
        {
            return StaticShadePreset.OledConservative;
        }

        return StaticShadePreset.OledBalanced;
    }

    private static bool NearlyEqual(double first, double second) =>
        Math.Abs(first - second) < 0.000001;
}
