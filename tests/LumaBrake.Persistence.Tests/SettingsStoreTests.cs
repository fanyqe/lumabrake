using LumaBrake.Persistence;
using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Color;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Hardware;
using LumaBrake.Domain.Profiles;
using LumaBrake.Domain.Rules;
using LumaBrake.Domain.Schedules;

namespace LumaBrake.Persistence.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lumabrake-settings-{Guid.NewGuid():N}");

    [Fact]
    public void Load_ReturnsDefaultsWhenSettingsFileIsMissing()
    {
        var settings = new SettingsStore(rootDirectory: _root).Load();

        Assert.Equal(SettingsMigration.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.True(settings.Enabled);
        Assert.Equal(0.64, settings.UserMaximumOpacity, precision: 12);
        Assert.Equal(0, settings.ManualDimmingOpacity, precision: 12);
        Assert.Empty(settings.MonitorManualDimming.Entries);
        Assert.Equal(ProtectionStrengthPreset.Balanced, settings.ProtectionTuning.Strength);
        Assert.Equal(ProtectionResponsePreset.Balanced, settings.ProtectionTuning.Response);
        Assert.Equal(LocalProtectionPreset.Balanced, settings.ProtectionTuning.LocalProtection);
        Assert.Equal(LocalMaskFitPreset.Precise, settings.ProtectionTuning.LocalMaskFit);
        Assert.Equal(WhitePageProtectionPreset.Normal, settings.ProtectionTuning.WhitePageProtection);
        Assert.Equal(StaticShadePreset.OledBalanced, settings.ProtectionTuning.StaticShadeProfile);
        Assert.False(settings.Tint.Enabled);
        Assert.False(settings.NightColor.Enabled);
        Assert.Equal(NightColorScheduleMode.Manual, settings.NightColor.ScheduleMode);
        Assert.Empty(settings.MonitorTints.Entries);
        Assert.False(settings.Schedules.Enabled);
        Assert.Empty(settings.Schedules.Entries);
        Assert.True(settings.StartupRestoration.Enabled);
        Assert.True(settings.StartupRestoration.ApplySchedules);
        Assert.False(settings.FocusDimming.Enabled);
        Assert.Equal(0.16, settings.FocusDimming.Opacity, precision: 12);
        Assert.Equal(3, settings.SpikeGuard.WarmupSamples);
        Assert.False(settings.SpikeGuard.LocalizedSpikeDetectionEnabled);
        Assert.Equal(3, settings.FlashGuard.MinimumTransitions);
        Assert.Equal(3, settings.FlashGuard.EarlyLocalizedTransitions);
        Assert.Equal(0.0015, settings.FlashGuard.MinimumAffectedTileRatio, precision: 12);
        Assert.Equal(0.35, settings.FlashGuard.EarlyLocalizedMaximumAffectedTileRatio, precision: 12);
        Assert.Equal(0.08, settings.FlashGuard.SignalTilePixelRatioThreshold, precision: 12);
        Assert.Equal(0.004, settings.FlashGuard.RegionalSignalPadding, precision: 12);
        Assert.Equal(TimeSpan.FromMilliseconds(500), settings.FlashGuard.Cooldown);
        Assert.Equal(TimeSpan.FromMilliseconds(450), settings.AdaptiveContent.MinimumBrightDuration);
        Assert.Equal(0.35, settings.AdaptiveContent.SmoothingFactor, precision: 12);
        Assert.Empty(settings.Profiles.Entries);
        Assert.True(settings.ApplicationRules.Enabled);
        Assert.Empty(settings.ApplicationRules.Entries);
        Assert.True(settings.FlashGuard.RedFlashDetectionEnabled);
        Assert.Equal(0.35, settings.FlashGuard.RedDominanceThreshold, precision: 12);
        Assert.Equal(0.35, settings.FlashGuard.RedDominantPixelRatioThreshold, precision: 12);
        Assert.True(settings.StaticShade.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(2), settings.StaticShade.MinimumStaticDuration);
        Assert.Equal(TimeSpan.FromSeconds(24), settings.StaticShade.FullOpacityDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(120), settings.StaticShade.Hold);
        Assert.Equal(TimeSpan.FromMilliseconds(260), settings.StaticShade.Release);
        Assert.Equal(0.18, settings.StaticShade.MinimumExposureScore, precision: 12);
        Assert.Equal(0.72, settings.StaticShade.BrightTilePercentile95Threshold, precision: 12);
        Assert.False(settings.HardwareBrightness.Enabled);
        Assert.False(settings.HardwareBrightness.AllowWrites);
        Assert.Empty(settings.HardwareBrightness.BlacklistedDisplayDeviceNames);
    }

    [Fact]
    public void Save_And_Load_RoundTripsSettings()
    {
        var store = new SettingsStore(rootDirectory: _root);
        store.Save(new LumaBrakeSettings
        {
            Enabled = false,
            UserMaximumOpacity = 0.25,
            ManualDimmingOpacity = 0.18,
            ProtectionTuning = new ProtectionTuningOptions
            {
                Strength = ProtectionStrengthPreset.Strong,
                Response = ProtectionResponsePreset.Fast,
                LocalProtection = LocalProtectionPreset.Strong,
                LocalMaskFit = LocalMaskFitPreset.Balanced,
                WhitePageProtection = WhitePageProtectionPreset.Strong,
                StaticShadeProfile = StaticShadePreset.OledAggressive
            },
            MonitorManualDimming = new MonitorManualDimmingOptions
            {
                Entries =
                [
                    new MonitorManualDimmingOverride
                    {
                        MonitorId = @"\\.\DISPLAY2",
                        Opacity = 0.21
                    }
                ]
            },
            FlashGuard = new FlashDetectorOptions
            {
                RedFlashDetectionEnabled = false,
                RedDominanceThreshold = 0.42,
                RedDominantPixelRatioThreshold = 0.48
            },
            StaticShade = StaticShadeOptions.ForPreset(StaticShadePreset.OledAggressive),
            NightColor = new NightColorOptions
            {
                Enabled = true,
                ScheduleMode = NightColorScheduleMode.Custom,
                DayTemperatureKelvin = 6500,
                NightTemperatureKelvin = 3300,
                Brightness = 0.82,
                CustomStartMinuteOfDay = 21 * 60,
                CustomEndMinuteOfDay = 6 * 60,
                TransitionMode = NightColorTransitionMode.Instant,
                ExcludedProcessNames = ["vlc"]
            },
            Schedules = new ScheduleOptions
            {
                Enabled = true,
                Entries =
                [
                    new ScheduledEffect
                    {
                        Id = "night",
                        Name = "Night",
                        StartMinuteOfDay = 22 * 60,
                        EndMinuteOfDay = 7 * 60,
                        ManualDimmingOpacity = 0.22,
                        Tint = new TintOptions { Enabled = true, Opacity = 0.10 }
                    }
                ]
            },
            MonitorTints = new MonitorTintOptions
            {
                Entries =
                [
                    new MonitorTintOverride
                    {
                        MonitorId = @"\\.\DISPLAY2",
                        Tint = new TintOptions { Enabled = true, Opacity = 0.16 }
                    }
                ]
            },
            FocusDimming = new FocusDimmingOptions
            {
                Enabled = true,
                Opacity = 0.18
            },
            Profiles = new ProfileOptions
            {
                Entries =
                [
                    new LumaBrakeProfile
                    {
                        Id = "work",
                        Name = "Work",
                        ManualDimmingOpacity = 0.20,
                        MonitorManualDimming = new MonitorManualDimmingOptions
                        {
                            Entries = [new MonitorManualDimmingOverride { MonitorId = "profile-display", Opacity = 0.19 }]
                        },
                        FocusDimming = new FocusDimmingOptions { Enabled = true, Opacity = 0.16 }
                    }
                ]
            },
            ApplicationRules = new ApplicationRuleOptions
            {
                Entries =
                [
                    new ApplicationRule
                    {
                        Id = "browser-video",
                        Name = "Browser Video",
                        ProcessName = "chrome",
                        WindowTitleContains = "Video",
                        ExcludeAutomaticEffects = true
                    }
                ]
            },
            HardwareBrightness = new HardwareBrightnessOptions
            {
                Enabled = true,
                AllowWrites = false,
                BlacklistedDisplayDeviceNames = [@"\\.\DISPLAY3"]
            }
        });

        var loaded = store.Load();

        Assert.Equal(SettingsMigration.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.False(loaded.Enabled);
        Assert.Equal(0.25, loaded.UserMaximumOpacity, precision: 12);
        Assert.Equal(0.18, loaded.ManualDimmingOpacity, precision: 12);
        Assert.Equal(ProtectionStrengthPreset.Strong, loaded.ProtectionTuning.Strength);
        Assert.Equal(ProtectionResponsePreset.Fast, loaded.ProtectionTuning.Response);
        Assert.Equal(LocalProtectionPreset.Strong, loaded.ProtectionTuning.LocalProtection);
        Assert.Equal(LocalMaskFitPreset.Balanced, loaded.ProtectionTuning.LocalMaskFit);
        Assert.Equal(WhitePageProtectionPreset.Strong, loaded.ProtectionTuning.WhitePageProtection);
        Assert.Equal(StaticShadePreset.OledAggressive, loaded.ProtectionTuning.StaticShadeProfile);
        var monitorManualDimming = Assert.Single(loaded.MonitorManualDimming.Entries);
        Assert.Equal(@"\\.\DISPLAY2", monitorManualDimming.MonitorId);
        Assert.Equal(0.21, monitorManualDimming.Opacity, precision: 12);
        Assert.False(loaded.FlashGuard.RedFlashDetectionEnabled);
        Assert.Equal(0.42, loaded.FlashGuard.RedDominanceThreshold, precision: 12);
        Assert.Equal(0.48, loaded.FlashGuard.RedDominantPixelRatioThreshold, precision: 12);
        Assert.Equal(TimeSpan.FromSeconds(1), loaded.StaticShade.MinimumStaticDuration);
        Assert.Equal(TimeSpan.FromSeconds(18), loaded.StaticShade.FullOpacityDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(90), loaded.StaticShade.Hold);
        Assert.Equal(TimeSpan.FromMilliseconds(220), loaded.StaticShade.Release);
        Assert.Equal(0.10, loaded.StaticShade.MinimumExposureScore, precision: 12);
        Assert.Equal(0.44, loaded.StaticShade.MaximumOpacity, precision: 12);
        Assert.True(loaded.NightColor.Enabled);
        Assert.Equal(NightColorScheduleMode.Custom, loaded.NightColor.ScheduleMode);
        Assert.Equal(3300, loaded.NightColor.NightTemperatureKelvin);
        Assert.Equal(0.82, loaded.NightColor.Brightness, precision: 12);
        Assert.Equal(21 * 60, loaded.NightColor.CustomStartMinuteOfDay);
        Assert.Equal(6 * 60, loaded.NightColor.CustomEndMinuteOfDay);
        Assert.Equal(NightColorTransitionMode.Instant, loaded.NightColor.TransitionMode);
        Assert.Equal(["vlc"], loaded.NightColor.ExcludedProcessNames);
        Assert.True(loaded.Schedules.Enabled);
        var schedule = Assert.Single(loaded.Schedules.Entries);
        Assert.Equal("night", schedule.Id);
        Assert.Equal(0.22, schedule.ManualDimmingOpacity, precision: 12);
        Assert.True(schedule.Tint.Enabled);
        var monitorTint = Assert.Single(loaded.MonitorTints.Entries);
        Assert.Equal(@"\\.\DISPLAY2", monitorTint.MonitorId);
        Assert.Equal(0.16, monitorTint.Tint.Opacity, precision: 12);
        Assert.True(loaded.FocusDimming.Enabled);
        Assert.Equal(0.18, loaded.FocusDimming.Opacity, precision: 12);
        var profile = Assert.Single(loaded.Profiles.Entries);
        Assert.Equal("work", profile.Id);
        Assert.Equal(0.20, profile.ManualDimmingOpacity, precision: 12);
        var profileMonitorManualDimming = Assert.Single(profile.MonitorManualDimming.Entries);
        Assert.Equal("profile-display", profileMonitorManualDimming.MonitorId);
        Assert.Equal(0.19, profileMonitorManualDimming.Opacity, precision: 12);
        Assert.True(profile.FocusDimming.Enabled);
        var rule = Assert.Single(loaded.ApplicationRules.Entries);
        Assert.Equal("browser-video", rule.Id);
        Assert.Equal("chrome", rule.ProcessName);
        Assert.Equal("Video", rule.WindowTitleContains);
        Assert.True(rule.ExcludeAutomaticEffects);
        Assert.True(loaded.HardwareBrightness.Enabled);
        Assert.False(loaded.HardwareBrightness.AllowWrites);
        Assert.Equal([@"\\.\DISPLAY3"], loaded.HardwareBrightness.BlacklistedDisplayDeviceNames);
    }

    [Fact]
    public void Load_BackfillsScheduleDefaultsForOlderSettingsJson()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "settings.json"),
            """
            {
              "schemaVersion": 7,
              "enabled": false,
              "userMaximumOpacity": 0.40,
              "manualDimmingOpacity": 0.12
            }
            """);

        var loaded = new SettingsStore(rootDirectory: _root).Load();

        Assert.Equal(SettingsMigration.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.False(loaded.Enabled);
        Assert.Equal(0.40, loaded.UserMaximumOpacity, precision: 12);
        Assert.Equal(0.12, loaded.ManualDimmingOpacity, precision: 12);
        Assert.Equal(ProtectionStrengthPreset.Gentle, loaded.ProtectionTuning.Strength);
        Assert.Equal(LocalMaskFitPreset.Precise, loaded.ProtectionTuning.LocalMaskFit);
        Assert.False(loaded.Schedules.Enabled);
        Assert.Empty(loaded.Schedules.Entries);
        Assert.False(loaded.NightColor.Enabled);
        Assert.Equal(NightColorScheduleMode.Manual, loaded.NightColor.ScheduleMode);
        Assert.Equal(3400, loaded.NightColor.NightTemperatureKelvin);
        Assert.Empty(loaded.MonitorManualDimming.Entries);
        Assert.Empty(loaded.MonitorTints.Entries);
        Assert.True(loaded.StartupRestoration.Enabled);
        Assert.False(loaded.FocusDimming.Enabled);
        Assert.Equal(0.16, loaded.FocusDimming.Opacity, precision: 12);
        Assert.Empty(loaded.Profiles.Entries);
        Assert.True(loaded.ApplicationRules.Enabled);
        Assert.Empty(loaded.ApplicationRules.Entries);
        Assert.True(loaded.FlashGuard.RedFlashDetectionEnabled);
        Assert.Equal(3, loaded.SpikeGuard.WarmupSamples);
        Assert.False(loaded.SpikeGuard.LocalizedSpikeDetectionEnabled);
        Assert.Equal(3, loaded.FlashGuard.MinimumTransitions);
        Assert.Equal(3, loaded.FlashGuard.EarlyLocalizedTransitions);
        Assert.Equal(0.0015, loaded.FlashGuard.MinimumAffectedTileRatio, precision: 12);
        Assert.Equal(TimeSpan.FromMilliseconds(500), loaded.FlashGuard.Cooldown);
        Assert.Equal(TimeSpan.FromMilliseconds(450), loaded.AdaptiveContent.MinimumBrightDuration);
        Assert.Equal(0.35, loaded.AdaptiveContent.SmoothingFactor, precision: 12);
        Assert.Equal(0.35, loaded.FlashGuard.RedDominanceThreshold, precision: 12);
        Assert.Equal(0.35, loaded.FlashGuard.RedDominantPixelRatioThreshold, precision: 12);
        Assert.Equal(TimeSpan.FromSeconds(2), loaded.StaticShade.MinimumStaticDuration);
        Assert.Equal(TimeSpan.FromSeconds(24), loaded.StaticShade.FullOpacityDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(120), loaded.StaticShade.Hold);
        Assert.Equal(TimeSpan.FromMilliseconds(260), loaded.StaticShade.Release);
        Assert.Equal(0.18, loaded.StaticShade.MinimumExposureScore, precision: 12);
        Assert.Equal(0.72, loaded.StaticShade.BrightTilePercentile95Threshold, precision: 12);
        Assert.False(loaded.HardwareBrightness.Enabled);
        Assert.False(loaded.HardwareBrightness.AllowWrites);
        Assert.False(loaded.StartupRestoration.HardwareBrightness.Enabled);
    }

    [Fact]
    public void Load_NormalizesUnsetSchemaVersion()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "settings.json"),
            """
            {
              "schemaVersion": 0,
              "manualDimmingOpacity": 0.12
            }
            """);

        var loaded = new SettingsStore(rootDirectory: _root).Load();

        Assert.Equal(SettingsMigration.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(0.12, loaded.ManualDimmingOpacity, precision: 12);
    }

    [Fact]
    public void Load_MigratesLegacyResponsiveDefaultsWithoutOverwritingCustomValues()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "settings.json"),
            """
            {
              "schemaVersion": 2,
              "spikeGuard": {
                "warmupSamples": 8,
                "baselineWindowSamples": 12
              },
              "flashGuard": {
                "minimumTransitions": 4,
                "cooldown": "00:00:00.6500000",
                "maximumOpacity": 0.50
              },
              "adaptiveContent": {
                "minimumBrightDuration": "00:00:00.9000000",
                "smoothingFactor": 0.20,
                "maximumOpacity": 0.20
              }
            }
            """);

        var loaded = new SettingsStore(rootDirectory: _root).Load();

        Assert.Equal(SettingsMigration.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(3, loaded.SpikeGuard.WarmupSamples);
        Assert.Equal(12, loaded.SpikeGuard.BaselineWindowSamples);
        Assert.Equal(3, loaded.FlashGuard.MinimumTransitions);
        Assert.Equal(0.0015, loaded.FlashGuard.MinimumAffectedTileRatio, precision: 12);
        Assert.Equal(TimeSpan.FromMilliseconds(500), loaded.FlashGuard.Cooldown);
        Assert.Equal(0.50, loaded.FlashGuard.MaximumOpacity, precision: 12);
        Assert.Equal(TimeSpan.FromMilliseconds(450), loaded.AdaptiveContent.MinimumBrightDuration);
        Assert.Equal(0.35, loaded.AdaptiveContent.SmoothingFactor, precision: 12);
        Assert.Equal(0.20, loaded.AdaptiveContent.MaximumOpacity, precision: 12);
    }

    [Fact]
    public void Load_PreservesLegacyCustomResponsiveValues()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "settings.json"),
            """
            {
              "schemaVersion": 2,
              "spikeGuard": {
                "warmupSamples": 5
              },
              "flashGuard": {
                "minimumTransitions": 5,
                "cooldown": "00:00:00.7000000"
              },
              "adaptiveContent": {
                "minimumBrightDuration": "00:00:00.6000000",
                "smoothingFactor": 0.25
              }
            }
            """);

        var loaded = new SettingsStore(rootDirectory: _root).Load();

        Assert.Equal(SettingsMigration.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(5, loaded.SpikeGuard.WarmupSamples);
        Assert.Equal(5, loaded.FlashGuard.MinimumTransitions);
        Assert.Equal(TimeSpan.FromMilliseconds(700), loaded.FlashGuard.Cooldown);
        Assert.Equal(TimeSpan.FromMilliseconds(600), loaded.AdaptiveContent.MinimumBrightDuration);
        Assert.Equal(0.25, loaded.AdaptiveContent.SmoothingFactor, precision: 12);
    }

    [Fact]
    public void Load_MigratesLegacyFlashAreaDefaultWithoutOverwritingCustomValue()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "settings.json"),
            """
            {
              "schemaVersion": 3,
              "flashGuard": {
                "minimumAffectedTileRatio": 0.03,
                "maximumOpacity": 0.50
              }
            }
            """);

        var loaded = new SettingsStore(rootDirectory: _root).Load();

        Assert.Equal(SettingsMigration.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(0.0015, loaded.FlashGuard.MinimumAffectedTileRatio, precision: 12);
        Assert.Equal(0.50, loaded.FlashGuard.MaximumOpacity, precision: 12);
    }

    [Fact]
    public void Load_PreservesCustomFlashAreaDefaultDuringPreciseMaskMigration()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "settings.json"),
            """
            {
              "schemaVersion": 3,
              "flashGuard": {
                "minimumAffectedTileRatio": 0.02
              }
            }
            """);

        var loaded = new SettingsStore(rootDirectory: _root).Load();

        Assert.Equal(SettingsMigration.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(0.02, loaded.FlashGuard.MinimumAffectedTileRatio, precision: 12);
    }

    [Fact]
    public void Load_MigratesLegacyStaticShadeTimingDefaultsWithoutOverwritingOtherValues()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "settings.json"),
            """
            {
              "schemaVersion": 5,
              "staticShade": {
                "minimumStaticDuration": "00:00:30",
                "fullOpacityDuration": "00:03:00",
                "maximumOpacity": 0.31
              }
            }
            """);

        var loaded = new SettingsStore(rootDirectory: _root).Load();

        Assert.Equal(SettingsMigration.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(TimeSpan.FromSeconds(2), loaded.StaticShade.MinimumStaticDuration);
        Assert.Equal(TimeSpan.FromSeconds(24), loaded.StaticShade.FullOpacityDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(120), loaded.StaticShade.Hold);
        Assert.Equal(TimeSpan.FromMilliseconds(260), loaded.StaticShade.Release);
        Assert.Equal(0.31, loaded.StaticShade.MaximumOpacity, precision: 12);
    }

    [Fact]
    public void Load_MigratesLegacyAggressiveStaticShadePresetTiming()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "settings.json"),
            """
            {
              "schemaVersion": 5,
              "staticShade": {
                "minimumStaticDuration": "00:00:12",
                "fullOpacityDuration": "00:01:30",
                "minimumExposureScore": 0.10,
                "maximumOpacity": 0.44
              }
            }
            """);

        var loaded = new SettingsStore(rootDirectory: _root).Load();

        Assert.Equal(SettingsMigration.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(TimeSpan.FromSeconds(1), loaded.StaticShade.MinimumStaticDuration);
        Assert.Equal(TimeSpan.FromSeconds(18), loaded.StaticShade.FullOpacityDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(90), loaded.StaticShade.Hold);
        Assert.Equal(TimeSpan.FromMilliseconds(220), loaded.StaticShade.Release);
        Assert.Equal(0.44, loaded.StaticShade.MaximumOpacity, precision: 12);
    }

    [Fact]
    public void Load_PreservesCustomStaticShadeTimingDuringReadableSurfaceMigration()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "settings.json"),
            """
            {
              "schemaVersion": 5,
              "staticShade": {
                "minimumStaticDuration": "00:00:07",
                "fullOpacityDuration": "00:00:40"
              }
            }
            """);

        var loaded = new SettingsStore(rootDirectory: _root).Load();

        Assert.Equal(SettingsMigration.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(TimeSpan.FromSeconds(7), loaded.StaticShade.MinimumStaticDuration);
        Assert.Equal(TimeSpan.FromSeconds(40), loaded.StaticShade.FullOpacityDuration);
    }

    [Fact]
    public void Load_MigratesLegacyStaticShadeReleaseDefaultsWithoutOverwritingCustomValues()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "settings.json"),
            """
            {
              "schemaVersion": 6,
              "staticShade": {
                "hold": "00:00:00.5000000",
                "release": "00:00:01.2000000",
                "minimumExposureScore": 0.22
              }
            }
            """);

        var loaded = new SettingsStore(rootDirectory: _root).Load();

        Assert.Equal(SettingsMigration.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(TimeSpan.FromMilliseconds(120), loaded.StaticShade.Hold);
        Assert.Equal(TimeSpan.FromMilliseconds(260), loaded.StaticShade.Release);
        Assert.Equal(0.22, loaded.StaticShade.MinimumExposureScore, precision: 12);
    }

    [Fact]
    public void Load_PreservesCustomStaticShadeReleaseDuringFastReleaseMigration()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "settings.json"),
            """
            {
              "schemaVersion": 6,
              "staticShade": {
                "hold": "00:00:00.2500000",
                "release": "00:00:00.7000000"
              }
            }
            """);

        var loaded = new SettingsStore(rootDirectory: _root).Load();

        Assert.Equal(SettingsMigration.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(TimeSpan.FromMilliseconds(250), loaded.StaticShade.Hold);
        Assert.Equal(TimeSpan.FromMilliseconds(700), loaded.StaticShade.Release);
    }

    [Fact]
    public void Load_InfersProtectionTuningForSchemaSevenSettings()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "settings.json"),
            """
            {
              "schemaVersion": 7,
              "userMaximumOpacity": 0.86,
              "spikeGuard": {
                "localizedSpikeDetectionEnabled": true,
                "maximumLocalizedChangedTileRatio": 0.52
              },
              "staticShade": {
                "enabled": true,
                "minimumStaticDuration": "00:00:01",
                "fullOpacityDuration": "00:00:18",
                "release": "00:00:00.2200000",
                "minimumAffectedTileRatio": 0.001,
                "maximumOpacity": 0.44
              },
              "adaptiveContent": {
                "minimumBrightDuration": "00:00:00.2600000",
                "minimumAverageLuminance": 0.36,
                "maximumOpacity": 0.42
              }
            }
            """);

        var loaded = new SettingsStore(rootDirectory: _root).Load();

        Assert.Equal(SettingsMigration.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(ProtectionStrengthPreset.Maximum, loaded.ProtectionTuning.Strength);
        Assert.Equal(ProtectionResponsePreset.Fast, loaded.ProtectionTuning.Response);
        Assert.Equal(LocalProtectionPreset.Aggressive, loaded.ProtectionTuning.LocalProtection);
        Assert.Equal(LocalMaskFitPreset.Precise, loaded.ProtectionTuning.LocalMaskFit);
        Assert.Equal(WhitePageProtectionPreset.Strong, loaded.ProtectionTuning.WhitePageProtection);
        Assert.Equal(StaticShadePreset.OledAggressive, loaded.ProtectionTuning.StaticShadeProfile);
    }

    [Fact]
    public void Save_WritesCurrentSchemaVersion()
    {
        var store = new SettingsStore(rootDirectory: _root);

        store.Save(new LumaBrakeSettings { SchemaVersion = 0, ManualDimmingOpacity = 0.12 });

        var json = File.ReadAllText(Path.Combine(_root, "settings.json"));
        Assert.Contains("\"schemaVersion\": 10", json);
        Assert.Equal(SettingsMigration.CurrentSchemaVersion, store.Load().SchemaVersion);
    }

    [Fact]
    public void Save_ReplacesExistingSettingsWithoutLeavingTemporaryFiles()
    {
        var store = new SettingsStore(rootDirectory: _root);
        store.Save(new LumaBrakeSettings { ManualDimmingOpacity = 0.12 });

        store.Save(new LumaBrakeSettings { ManualDimmingOpacity = 0.34 });

        Assert.Equal(0.34, store.Load().ManualDimmingOpacity, precision: 12);
        Assert.Empty(Directory.GetFiles(_root, "settings.json.tmp-*"));
    }

    [Fact]
    public void StartupRestoration_AppliesActiveScheduleTintWithoutDuplicatingDimming()
    {
        var settings = new LumaBrakeSettings
        {
            ManualDimmingOpacity = 0.05,
            Tint = new TintOptions { Enabled = false },
            StartupRestoration = new StartupRestorationOptions
            {
                HardwareBrightness = new HardwareBrightnessTarget
                {
                    Enabled = true,
                    DisplayDeviceName = @"\\.\DISPLAY1",
                    PhysicalMonitorIndex = 0,
                    Brightness = 80
                }
            },
            Schedules = new ScheduleOptions
            {
                Enabled = true,
                Entries =
                [
                    new ScheduledEffect
                    {
                        Id = "night",
                        StartMinuteOfDay = 22 * 60,
                        EndMinuteOfDay = 7 * 60,
                        ManualDimmingOpacity = 0.24,
                        HardwareBrightness = new HardwareBrightnessTarget
                        {
                            Enabled = true,
                            DisplayDeviceName = @"\\.\DISPLAY2",
                            PhysicalMonitorIndex = 1,
                            Brightness = 60,
                            UseSoftwareFallback = true
                        },
                        Tint = new TintOptions { Enabled = true, Red = 255, Green = 214, Blue = 170, Opacity = 0.18 }
                    }
                ]
            }
        };

        var restored = StartupRestorationPolicy.Apply(
            settings,
            new DateTimeOffset(2026, 7, 26, 23, 0, 0, TimeSpan.FromHours(3)));

        Assert.Equal(0.05, restored.ManualDimmingOpacity, precision: 12);
        Assert.True(restored.Tint.Enabled);
        Assert.Equal(0.18, restored.Tint.Opacity, precision: 12);
        Assert.Equal(@"\\.\DISPLAY2", restored.StartupRestoration.HardwareBrightness.DisplayDeviceName);
        Assert.Equal(1, restored.StartupRestoration.HardwareBrightness.PhysicalMonitorIndex);
        Assert.Equal(60, restored.StartupRestoration.HardwareBrightness.Brightness);
        Assert.True(restored.StartupRestoration.HardwareBrightness.UseSoftwareFallback);
    }

    [Fact]
    public void StartupRestoration_KeepsPersistedEffectsWhenNoScheduleIsActive()
    {
        var settings = new LumaBrakeSettings
        {
            ManualDimmingOpacity = 0.14,
            Tint = new TintOptions { Enabled = true, Opacity = 0.08 },
            StartupRestoration = new StartupRestorationOptions
            {
                HardwareBrightness = new HardwareBrightnessTarget
                {
                    Enabled = true,
                    DisplayDeviceName = @"\\.\DISPLAY3",
                    PhysicalMonitorIndex = 2,
                    Brightness = 45
                }
            },
            Schedules = new ScheduleOptions
            {
                Enabled = true,
                Entries =
                [
                    new ScheduledEffect
                    {
                        StartMinuteOfDay = 22 * 60,
                        EndMinuteOfDay = 7 * 60,
                        ManualDimmingOpacity = 0.24
                    }
                ]
            }
        };

        var restored = StartupRestorationPolicy.Apply(
            settings,
            new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.FromHours(3)));

        Assert.Equal(0.14, restored.ManualDimmingOpacity, precision: 12);
        Assert.True(restored.Tint.Enabled);
        Assert.Equal(0.08, restored.Tint.Opacity, precision: 12);
        Assert.Equal(@"\\.\DISPLAY3", restored.StartupRestoration.HardwareBrightness.DisplayDeviceName);
        Assert.Equal(2, restored.StartupRestoration.HardwareBrightness.PhysicalMonitorIndex);
        Assert.Equal(45, restored.StartupRestoration.HardwareBrightness.Brightness);
    }

    [Fact]
    public void StartupRestoration_DisabledClearsSoftwareEffectsForStartup()
    {
        var settings = new LumaBrakeSettings
        {
            ManualDimmingOpacity = 0.33,
            MonitorManualDimming = new MonitorManualDimmingOptions
            {
                Entries = [new MonitorManualDimmingOverride { MonitorId = @"\\.\DISPLAY2", Opacity = 0.28 }]
            },
            Tint = new TintOptions { Enabled = true, Opacity = 0.20 },
            StartupRestoration = new StartupRestorationOptions
            {
                Enabled = false,
                HardwareBrightness = new HardwareBrightnessTarget
                {
                    Enabled = true,
                    DisplayDeviceName = @"\\.\DISPLAY4",
                    PhysicalMonitorIndex = 0,
                    Brightness = 50
                }
            }
        };

        var restored = StartupRestorationPolicy.Apply(
            settings,
            new DateTimeOffset(2026, 7, 26, 23, 0, 0, TimeSpan.FromHours(3)));

        Assert.Equal(0, restored.ManualDimmingOpacity, precision: 12);
        Assert.Empty(restored.MonitorManualDimming.Entries);
        Assert.False(restored.Tint.Enabled);
        Assert.False(restored.StartupRestoration.HardwareBrightness.Enabled);
    }

    [Fact]
    public void Load_QuarantinesCorruptJsonAndReturnsDefaults()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "settings.json"), "{not-json");

        var loaded = new SettingsStore(rootDirectory: _root).Load();

        Assert.True(loaded.Enabled);
        Assert.False(File.Exists(Path.Combine(_root, "settings.json")));
        Assert.Single(Directory.GetFiles(_root, "settings.json.corrupt-*"));
    }

    [Fact]
    public void Load_QuarantinesRepeatedCorruptSettingsWithoutOverwritingEarlierBackup()
    {
        Directory.CreateDirectory(_root);
        var settingsPath = Path.Combine(_root, "settings.json");
        File.WriteAllText(settingsPath, "{not-json");
        _ = new SettingsStore(rootDirectory: _root).Load();

        File.WriteAllText(settingsPath, "{still-not-json");
        _ = new SettingsStore(rootDirectory: _root).Load();

        Assert.Equal(2, Directory.GetFiles(_root, "settings.json.corrupt-*").Length);
    }

    [Fact]
    public void SafeMode_DisablesEffectsAndClearsManualDimming()
    {
        var settings = new LumaBrakeSettings
        {
            Enabled = true,
            UserMaximumOpacity = 0.64,
            ManualDimmingOpacity = 0.35,
            MonitorManualDimming = new MonitorManualDimmingOptions
            {
                Entries = [new MonitorManualDimmingOverride { MonitorId = @"\\.\DISPLAY2", Opacity = 0.28 }]
            },
            HardwareBrightness = new HardwareBrightnessOptions
            {
                Enabled = true,
                AllowWrites = true,
                BlacklistedDisplayDeviceNames = [@"\\.\DISPLAY4"]
            }
        };

        var safe = SafeModeSettings.Apply(settings);

        Assert.False(safe.Enabled);
        Assert.Equal(0.25, safe.UserMaximumOpacity, precision: 12);
        Assert.Equal(0, safe.ManualDimmingOpacity, precision: 12);
        Assert.Empty(safe.MonitorManualDimming.Entries);
        Assert.False(safe.HardwareBrightness.Enabled);
        Assert.False(safe.HardwareBrightness.AllowWrites);
        Assert.Equal([@"\\.\DISPLAY4"], safe.HardwareBrightness.BlacklistedDisplayDeviceNames);
    }

    [Fact]
    public void SafeMode_DoesNotRaiseExistingMaximumDimming()
    {
        var settings = new LumaBrakeSettings { UserMaximumOpacity = 0.12 };

        var safe = SafeModeSettings.Apply(settings);

        Assert.Equal(0.12, safe.UserMaximumOpacity, precision: 12);
    }

    [Theory]
    [InlineData("--safe-mode", true)]
    [InlineData("--SAFE-MODE", true)]
    [InlineData("--other", false)]
    public void SafeMode_DetectsStartupArgument(string argument, bool expected)
    {
        Assert.Equal(expected, SafeModeSettings.IsRequested([argument]));
    }

    [Theory]
    [InlineData("--background", true)]
    [InlineData("--minimized", true)]
    [InlineData("--tray", true)]
    [InlineData("--other", false)]
    public void AppLaunchOptions_DetectsBackgroundStartupArgument(string argument, bool expected)
    {
        Assert.Equal(expected, AppLaunchOptions.FromArgs([argument]).StartInBackground);
    }

    [Theory]
    [InlineData("--smoke-startup", true)]
    [InlineData("--SMOKE-STARTUP", true)]
    [InlineData("--other", false)]
    public void AppLaunchOptions_DetectsSmokeStartupArgument(string argument, bool expected)
    {
        Assert.Equal(expected, AppLaunchOptions.FromArgs([argument]).SmokeStartupRequested);
    }

    [Fact]
    public void AppLaunchOptions_KeepsSafeModeAndBackgroundIndependent()
    {
        var options = AppLaunchOptions.FromArgs(["--safe-mode", "--background"]);

        Assert.True(options.SafeModeRequested);
        Assert.True(options.StartInBackground);
        Assert.False(options.SmokeStartupRequested);
    }

    [Fact]
    public void HardwareBrightnessStateStore_RoundTripsWriteAttemptsCaseInsensitively()
    {
        var store = new HardwareBrightnessStateStore(rootDirectory: _root);
        var attemptedAt = new DateTimeOffset(2026, 7, 26, 3, 20, 0, TimeSpan.Zero);

        store.RecordWriteAttempt(@"\\.\DISPLAY1#0", attemptedAt);

        var loaded = store.Load();
        Assert.Equal(attemptedAt, loaded.LastWriteAttemptAt(@"\\.\display1#0"));
    }

    [Fact]
    public void HardwareBrightnessStateStore_ReplacesExistingStateWithoutLeavingTemporaryFiles()
    {
        var store = new HardwareBrightnessStateStore(rootDirectory: _root);
        store.RecordWriteAttempt(@"\\.\DISPLAY1#0", new DateTimeOffset(2026, 7, 26, 3, 20, 0, TimeSpan.Zero));

        store.RecordWriteAttempt(@"\\.\DISPLAY2#1", new DateTimeOffset(2026, 7, 26, 3, 25, 0, TimeSpan.Zero));

        var loaded = store.Load();
        Assert.NotNull(loaded.LastWriteAttemptAt(@"\\.\DISPLAY1#0"));
        Assert.NotNull(loaded.LastWriteAttemptAt(@"\\.\DISPLAY2#1"));
        Assert.Empty(Directory.GetFiles(_root, "hardware-brightness-state.json.tmp-*"));
    }

    [Fact]
    public void HardwareBrightnessStateStore_QuarantinesCorruptJsonAndReturnsDefaults()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "hardware-brightness-state.json"), "{not-json");

        var loaded = new HardwareBrightnessStateStore(rootDirectory: _root).Load();

        Assert.Empty(loaded.LastWriteAttempts);
        Assert.False(File.Exists(Path.Combine(_root, "hardware-brightness-state.json")));
        Assert.Single(Directory.GetFiles(_root, "hardware-brightness-state.json.corrupt-*"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
