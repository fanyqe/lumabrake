using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Color;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Hardware;
using LumaBrake.Domain.Profiles;
using LumaBrake.Domain.Schedules;
using LumaBrake.Persistence;

namespace LumaBrake.Persistence.Tests;

public sealed class ProfileSettingsPolicyTests
{
    [Fact]
    public void Capture_StoresSoftwareDimmingTintAndFocusSettings()
    {
        var profile = ProfileSettingsPolicy.Capture(
            "work",
            "Work",
            new LumaBrakeSettings
            {
                UserMaximumOpacity = 0.58,
                ManualDimmingOpacity = 0.22,
                ProtectionTuning = new ProtectionTuningOptions
                {
                    Strength = ProtectionStrengthPreset.Strong,
                    Response = ProtectionResponsePreset.Fast,
                    LocalProtection = LocalProtectionPreset.Strong,
                    LocalMaskFit = LocalMaskFitPreset.Balanced,
                    WhitePageProtection = WhitePageProtectionPreset.Strong,
                    StaticShadeProfile = StaticShadePreset.StaticHud
                },
                MonitorManualDimming = new MonitorManualDimmingOptions
                {
                    Entries =
                    [
                        new MonitorManualDimmingOverride { MonitorId = "monitor-1", Opacity = 0.17 }
                    ]
                },
                Tint = new TintOptions { Enabled = true, Red = 255, Green = 214, Blue = 170, Opacity = 0.12 },
                NightColor = new NightColorOptions
                {
                    Enabled = true,
                    ScheduleMode = NightColorScheduleMode.Custom,
                    NightTemperatureKelvin = 3300,
                    Brightness = 0.82
                },
                MonitorTints = new MonitorTintOptions
                {
                    Entries =
                    [
                        new MonitorTintOverride { MonitorId = "monitor-1", Tint = new TintOptions { Enabled = true, Opacity = 0.14 } }
                    ]
                },
                FocusDimming = new FocusDimmingOptions { Enabled = true, Opacity = 0.18 },
                StartupRestoration = new StartupRestorationOptions
                {
                    HardwareBrightness = new HardwareBrightnessTarget
                    {
                        Enabled = true,
                        DisplayDeviceName = @"\\.\DISPLAY2",
                        PhysicalMonitorIndex = 1,
                        Brightness = 70,
                        UseSoftwareFallback = true
                    }
                }
            });

        Assert.Equal("work", profile.Id);
        Assert.Equal("Work", profile.Name);
        Assert.Equal(0.58, profile.UserMaximumOpacity, precision: 12);
        Assert.Equal(0.22, profile.ManualDimmingOpacity, precision: 12);
        Assert.Equal(ProtectionStrengthPreset.Strong, profile.ProtectionTuning.Strength);
        Assert.Equal(ProtectionResponsePreset.Fast, profile.ProtectionTuning.Response);
        Assert.Equal(LocalProtectionPreset.Strong, profile.ProtectionTuning.LocalProtection);
        Assert.Equal(LocalMaskFitPreset.Balanced, profile.ProtectionTuning.LocalMaskFit);
        Assert.Equal(WhitePageProtectionPreset.Strong, profile.ProtectionTuning.WhitePageProtection);
        Assert.Equal(StaticShadePreset.StaticHud, profile.ProtectionTuning.StaticShadeProfile);
        var monitorManualDimming = Assert.Single(profile.MonitorManualDimming.Entries);
        Assert.Equal("monitor-1", monitorManualDimming.MonitorId);
        Assert.Equal(0.17, monitorManualDimming.Opacity, precision: 12);
        Assert.True(profile.Tint.Enabled);
        Assert.Equal(0.12, profile.Tint.Opacity, precision: 12);
        Assert.True(profile.NightColor.Enabled);
        Assert.Equal(NightColorScheduleMode.Custom, profile.NightColor.ScheduleMode);
        Assert.Equal(3300, profile.NightColor.NightTemperatureKelvin);
        Assert.Equal(0.82, profile.NightColor.Brightness, precision: 12);
        var monitorTint = Assert.Single(profile.MonitorTints.Entries);
        Assert.Equal("monitor-1", monitorTint.MonitorId);
        Assert.Equal(0.14, monitorTint.Tint.Opacity, precision: 12);
        Assert.True(profile.FocusDimming.Enabled);
        Assert.Equal(0.18, profile.FocusDimming.Opacity, precision: 12);
        Assert.True(profile.HardwareBrightness.Enabled);
        Assert.Equal(@"\\.\DISPLAY2", profile.HardwareBrightness.DisplayDeviceName);
        Assert.Equal(1, profile.HardwareBrightness.PhysicalMonitorIndex);
        Assert.Equal(70, profile.HardwareBrightness.Brightness);
        Assert.True(profile.HardwareBrightness.UseSoftwareFallback);
    }

    [Fact]
    public void Apply_ChangesSoftwareProfileAndProtectionTuningSettings()
    {
        var settings = new LumaBrakeSettings
        {
            Enabled = false,
            UserMaximumOpacity = 0.30,
            ManualDimmingOpacity = 0.05,
            MonitorManualDimming = new MonitorManualDimmingOptions
            {
                Entries = [new MonitorManualDimmingOverride { MonitorId = "old", Opacity = 0.04 }]
            },
            Tint = new TintOptions { Enabled = false },
            NightColor = new NightColorOptions { Enabled = false },
            MonitorTints = new MonitorTintOptions
            {
                Entries = [new MonitorTintOverride { MonitorId = "old" }]
            },
            FocusDimming = new FocusDimmingOptions { Enabled = false },
            Schedules = new ScheduleOptions { Enabled = true },
            HardwareBrightness = new HardwareBrightnessOptions { Enabled = true, AllowWrites = true }
        };

        var applied = ProfileSettingsPolicy.Apply(
            settings,
            new LumaBrakeProfile
            {
                Id = "night",
                UserMaximumOpacity = 0.62,
                ManualDimmingOpacity = 0.24,
                ProtectionTuning = new ProtectionTuningOptions
                {
                    Strength = ProtectionStrengthPreset.Maximum,
                    Response = ProtectionResponsePreset.Fast,
                    LocalProtection = LocalProtectionPreset.Aggressive,
                    LocalMaskFit = LocalMaskFitPreset.Expanded,
                    WhitePageProtection = WhitePageProtectionPreset.Strong,
                    StaticShadeProfile = StaticShadePreset.OledAggressive
                },
                MonitorManualDimming = new MonitorManualDimmingOptions
                {
                    Entries = [new MonitorManualDimmingOverride { MonitorId = "new", Opacity = 0.15 }]
                },
                Tint = new TintOptions { Enabled = true, Red = 255, Green = 214, Blue = 170, Opacity = 0.16 },
                NightColor = new NightColorOptions
                {
                    Enabled = true,
                    ScheduleMode = NightColorScheduleMode.Manual,
                    ManualTemperatureKelvin = 3200,
                    Brightness = 0.76
                },
                MonitorTints = new MonitorTintOptions
                {
                    Entries = [new MonitorTintOverride { MonitorId = "new", Tint = new TintOptions { Enabled = true, Opacity = 0.11 } }]
                },
                FocusDimming = new FocusDimmingOptions { Enabled = true, Opacity = 0.20 },
                HardwareBrightness = new HardwareBrightnessTarget
                {
                    Enabled = true,
                    DisplayDeviceName = @"\\.\DISPLAY3",
                    PhysicalMonitorIndex = 2,
                    Brightness = 45
                }
            });

        Assert.False(applied.Enabled);
        Assert.True(applied.Schedules.Enabled);
        Assert.True(applied.HardwareBrightness.Enabled);
        Assert.True(applied.HardwareBrightness.AllowWrites);
        Assert.Equal(0.62, applied.UserMaximumOpacity, precision: 12);
        Assert.Equal(ProtectionStrengthPreset.Maximum, applied.ProtectionTuning.Strength);
        Assert.Equal(ProtectionResponsePreset.Fast, applied.ProtectionTuning.Response);
        Assert.Equal(LocalProtectionPreset.Aggressive, applied.ProtectionTuning.LocalProtection);
        Assert.Equal(LocalMaskFitPreset.Expanded, applied.ProtectionTuning.LocalMaskFit);
        Assert.Equal(WhitePageProtectionPreset.Strong, applied.ProtectionTuning.WhitePageProtection);
        Assert.Equal(StaticShadePreset.OledAggressive, applied.ProtectionTuning.StaticShadeProfile);
        Assert.Equal(0.62, applied.StaticShade.MaximumOpacity, precision: 12);
        Assert.Equal(0.46, applied.AdaptiveContent.MaximumOpacity, precision: 12);
        Assert.Equal(0.24, applied.ManualDimmingOpacity, precision: 12);
        Assert.True(applied.NightColor.Enabled);
        Assert.Equal(NightColorScheduleMode.Manual, applied.NightColor.ScheduleMode);
        Assert.Equal(3200, applied.NightColor.ManualTemperatureKelvin);
        Assert.Equal(0.76, applied.NightColor.Brightness, precision: 12);
        var appliedMonitorManualDimming = Assert.Single(applied.MonitorManualDimming.Entries);
        Assert.Equal("new", appliedMonitorManualDimming.MonitorId);
        Assert.Equal(0.15, appliedMonitorManualDimming.Opacity, precision: 12);
        Assert.True(applied.Tint.Enabled);
        var appliedMonitorTint = Assert.Single(applied.MonitorTints.Entries);
        Assert.Equal("new", appliedMonitorTint.MonitorId);
        Assert.Equal(0.11, appliedMonitorTint.Tint.Opacity, precision: 12);
        Assert.True(applied.FocusDimming.Enabled);
        Assert.Equal(0.20, applied.FocusDimming.Opacity, precision: 12);
        Assert.True(applied.StartupRestoration.HardwareBrightness.Enabled);
        Assert.Equal(@"\\.\DISPLAY3", applied.StartupRestoration.HardwareBrightness.DisplayDeviceName);
        Assert.Equal(2, applied.StartupRestoration.HardwareBrightness.PhysicalMonitorIndex);
        Assert.Equal(45, applied.StartupRestoration.HardwareBrightness.Brightness);
    }
}
