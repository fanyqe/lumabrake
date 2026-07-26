using LumaBrake.Domain.Analysis;
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
                Tint = new TintOptions { Enabled = true, Red = 255, Green = 214, Blue = 170, Opacity = 0.12 },
                FocusDimming = new FocusDimmingOptions { Enabled = true, Opacity = 0.18 }
            });

        Assert.Equal("work", profile.Id);
        Assert.Equal("Work", profile.Name);
        Assert.Equal(0.58, profile.UserMaximumOpacity, precision: 12);
        Assert.Equal(0.22, profile.ManualDimmingOpacity, precision: 12);
        Assert.True(profile.Tint.Enabled);
        Assert.Equal(0.12, profile.Tint.Opacity, precision: 12);
        Assert.True(profile.FocusDimming.Enabled);
        Assert.Equal(0.18, profile.FocusDimming.Opacity, precision: 12);
    }

    [Fact]
    public void Apply_ChangesOnlySoftwareProfileSettings()
    {
        var settings = new LumaBrakeSettings
        {
            Enabled = false,
            UserMaximumOpacity = 0.30,
            ManualDimmingOpacity = 0.05,
            Tint = new TintOptions { Enabled = false },
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
                Tint = new TintOptions { Enabled = true, Red = 255, Green = 214, Blue = 170, Opacity = 0.16 },
                FocusDimming = new FocusDimmingOptions { Enabled = true, Opacity = 0.20 }
            });

        Assert.False(applied.Enabled);
        Assert.True(applied.Schedules.Enabled);
        Assert.True(applied.HardwareBrightness.Enabled);
        Assert.True(applied.HardwareBrightness.AllowWrites);
        Assert.Equal(0.62, applied.UserMaximumOpacity, precision: 12);
        Assert.Equal(0.24, applied.ManualDimmingOpacity, precision: 12);
        Assert.True(applied.Tint.Enabled);
        Assert.True(applied.FocusDimming.Enabled);
        Assert.Equal(0.20, applied.FocusDimming.Opacity, precision: 12);
    }
}
