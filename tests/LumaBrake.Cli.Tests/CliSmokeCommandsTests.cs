using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Color;
using LumaBrake.Persistence;

namespace LumaBrake.Cli.Tests;

public sealed class CliSmokeCommandsTests : IDisposable
{
    private readonly string _settingsRoot = Path.Combine(Path.GetTempPath(), $"lumabrake-cli-smoke-{Guid.NewGuid():N}");
    private readonly CliTestRunner _runner;

    public CliSmokeCommandsTests()
    {
        _runner = new CliTestRunner(_settingsRoot);
    }

    [Fact]
    public void BasicVisualCommands_UpdateSettings()
    {
        var status = RunCli("status");
        AssertSuccess(status);
        Assert.Contains("LumaBrake settings: enabled", status.StandardOutput, StringComparison.Ordinal);

        AssertSuccess(RunCli("disable"));
        Assert.False(LoadSettings().Enabled);

        AssertSuccess(RunCli("enable"));
        Assert.True(LoadSettings().Enabled);

        AssertSuccess(RunCli("dim", "25"));
        Assert.Equal(0.25, LoadSettings().ManualDimmingOpacity, precision: 12);

        AssertSuccess(RunCli("dim", "off"));
        Assert.Equal(0, LoadSettings().ManualDimmingOpacity, precision: 12);

        AssertSuccess(RunCli("tint", "warm", "10"));
        var warmTint = LoadSettings().Tint;
        Assert.True(warmTint.Enabled);
        Assert.Equal(0.10, warmTint.Opacity, precision: 12);

        AssertSuccess(RunCli("tint", "off"));
        Assert.False(LoadSettings().Tint.Enabled);

        AssertSuccess(RunCli("night-color", "status"));
        AssertSuccess(RunCli("night-color", "manual", "3400", "78"));
        var manualNightColor = LoadSettings().NightColor;
        Assert.True(manualNightColor.Enabled);
        Assert.Equal(NightColorScheduleMode.Manual, manualNightColor.ScheduleMode);
        Assert.Equal(3400, manualNightColor.ManualTemperatureKelvin);
        Assert.Equal(0.78, manualNightColor.Brightness, precision: 12);

        AssertSuccess(RunCli("night-color", "custom", "21:30", "06:15", "6500", "3200", "82", "instant", "5"));
        var customNightColor = LoadSettings().NightColor;
        Assert.Equal(NightColorScheduleMode.Custom, customNightColor.ScheduleMode);
        Assert.Equal(21 * 60 + 30, customNightColor.CustomStartMinuteOfDay);
        Assert.Equal(6 * 60 + 15, customNightColor.CustomEndMinuteOfDay);
        Assert.Equal(3200, customNightColor.NightTemperatureKelvin);
        Assert.Equal(0.82, customNightColor.Brightness, precision: 12);
        Assert.Equal(NightColorTransitionMode.Instant, customNightColor.TransitionMode);
        Assert.Equal(TimeSpan.FromMinutes(5), customNightColor.TransitionDuration);

        AssertSuccess(RunCli("night-color", "sun", "40.7128", "-74.0060", "6500", "3300", "85", "smooth", "20"));
        var sunNightColor = LoadSettings().NightColor;
        Assert.Equal(NightColorScheduleMode.SunriseSunset, sunNightColor.ScheduleMode);
        Assert.Equal(40.7128, sunNightColor.Latitude, precision: 12);
        Assert.Equal(-74.0060, sunNightColor.Longitude, precision: 12);
        AssertSuccess(RunCli("night-color", "strength", "55"));
        Assert.Equal(0.55, LoadSettings().NightColor.TintStrength, precision: 12);
        var locationStatus = RunCli("night-color", "status");
        AssertSuccess(locationStatus);
        Assert.Contains("Night color location: 40.7128, -74.006", locationStatus.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Night color strength: 55%", locationStatus.StandardOutput, StringComparison.Ordinal);

        AssertSuccess(RunCli("night-color", "pause", "30"));
        Assert.NotNull(LoadSettings().NightColor.PausedUntilUtc);
        AssertSuccess(RunCli("night-color", "resume"));
        Assert.Null(LoadSettings().NightColor.PausedUntilUtc);
        AssertSuccess(RunCli("night-color", "fullscreen", "on"));
        Assert.True(LoadSettings().NightColor.PauseInFullScreenApplications);
        var fullScreenStatus = RunCli("night-color", "status");
        AssertSuccess(fullScreenStatus);
        Assert.Contains("Night color full-screen pause: on", fullScreenStatus.StandardOutput, StringComparison.Ordinal);
        AssertSuccess(RunCli("night-color", "fullscreen", "off"));
        Assert.False(LoadSettings().NightColor.PauseInFullScreenApplications);
        AssertSuccess(RunCli("night-color", "off"));
        Assert.False(LoadSettings().NightColor.Enabled);

        var safeMode = RunCli("safe-mode");
        AssertSuccess(safeMode);
        Assert.Contains("Safe mode settings saved.", safeMode.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorOverrideCommands_UpdateSettingsWithoutNativeMonitorAccess()
    {
        AssertSuccess(RunCli("dim", "monitor", "list"));

        AssertSuccess(RunCli("dim", "monitor", "set", @"\\.\DISPLAY2", "35"));
        var dimming = Assert.Single(LoadSettings().MonitorManualDimming.Entries);
        Assert.Equal(@"\\.\DISPLAY2", dimming.MonitorId);
        Assert.Equal(0.35, dimming.Opacity, precision: 12);

        AssertSuccess(RunCli("dim", "monitor", "remove", @"\\.\DISPLAY2"));
        Assert.Empty(LoadSettings().MonitorManualDimming.Entries);

        AssertSuccess(RunCli("tint", "monitor", "list"));

        AssertSuccess(RunCli("tint", "monitor", "set", @"\\.\DISPLAY2", "neutral", "18"));
        var tint = Assert.Single(LoadSettings().MonitorTints.Entries);
        Assert.Equal(@"\\.\DISPLAY2", tint.MonitorId);
        Assert.True(tint.Tint.Enabled);
        Assert.Equal(0.18, tint.Tint.Opacity, precision: 12);

        AssertSuccess(RunCli("tint", "monitor", "remove", @"\\.\DISPLAY2"));
        Assert.Empty(LoadSettings().MonitorTints.Entries);
    }

    [Fact]
    public void FocusStaticShadeAndApplicationRuleCommands_UpdateSettings()
    {
        AssertSuccess(RunCli("focus", "status"));
        AssertSuccess(RunCli("focus", "enable"));
        AssertSuccess(RunCli("focus", "opacity", "22"));

        var focus = LoadSettings().FocusDimming;
        Assert.True(focus.Enabled);
        Assert.Equal(0.22, focus.Opacity, precision: 12);

        AssertSuccess(RunCli("focus", "disable"));
        Assert.False(LoadSettings().FocusDimming.Enabled);

        var presets = RunCli("static-shade", "list");
        AssertSuccess(presets);
        Assert.Contains("oled-balanced", presets.StandardOutput, StringComparison.Ordinal);

        AssertSuccess(RunCli("static-shade", "preset", "office"));
        Assert.True(LoadSettings().StaticShade.Enabled);
        Assert.Equal(StaticShadePreset.OfficeUse, LoadSettings().ProtectionTuning.StaticShadeProfile);

        var tuningChoices = RunCli("tuning", "list");
        AssertSuccess(tuningChoices);
        Assert.Contains("strength: gentle|balanced|strong|maximum", tuningChoices.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("mask-fit: precise|balanced|expanded", tuningChoices.StandardOutput, StringComparison.Ordinal);

        AssertSuccess(RunCli("tuning", "preset", "maximum"));
        var maximumTuning = LoadSettings();
        Assert.Equal(ProtectionStrengthPreset.Maximum, maximumTuning.ProtectionTuning.Strength);
        Assert.Equal(ProtectionResponsePreset.Fast, maximumTuning.ProtectionTuning.Response);
        Assert.Equal(LocalProtectionPreset.Aggressive, maximumTuning.ProtectionTuning.LocalProtection);
        Assert.Equal(LocalMaskFitPreset.Balanced, maximumTuning.ProtectionTuning.LocalMaskFit);
        Assert.Equal(WhitePageProtectionPreset.Strong, maximumTuning.ProtectionTuning.WhitePageProtection);
        Assert.Equal(0.88, maximumTuning.UserMaximumOpacity, precision: 12);
        Assert.Equal(0.62, maximumTuning.StaticShade.MaximumOpacity, precision: 12);
        Assert.True(maximumTuning.SpikeGuard.LocalizedSpikeDetectionEnabled);

        AssertSuccess(RunCli("tuning", "strength", "gentle"));
        Assert.Equal(ProtectionStrengthPreset.Gentle, LoadSettings().ProtectionTuning.Strength);

        AssertSuccess(RunCli("tuning", "response", "calm"));
        Assert.Equal(ProtectionResponsePreset.Calm, LoadSettings().ProtectionTuning.Response);

        AssertSuccess(RunCli("tuning", "mask-fit", "expanded"));
        Assert.Equal(LocalMaskFitPreset.Expanded, LoadSettings().ProtectionTuning.LocalMaskFit);
        Assert.Equal(LocalMaskFitPreset.Expanded, LoadSettings().StaticShade.RegionalMaskFit);

        AssertSuccess(RunCli("app-rule", "status"));
        AssertSuccess(RunCli("app-rule", "add", "video", "--process", "vlc.exe", "--name", "Video Player", "--night-color"));
        var rule = Assert.Single(LoadSettings().ApplicationRules.Entries);
        Assert.Equal("video", rule.Id);
        Assert.Equal("vlc.exe", rule.ProcessName);
        Assert.True(rule.ExcludeAutomaticEffects);
        Assert.True(rule.ExcludeNightColor);

        AssertSuccess(RunCli("app-rule", "disable-entry", "video"));
        Assert.False(LoadSettings().ApplicationRules.Entries.Single().Enabled);

        AssertSuccess(RunCli("app-rule", "enable-entry", "video"));
        Assert.True(LoadSettings().ApplicationRules.Entries.Single().Enabled);

        AssertSuccess(RunCli("app-rule", "remove", "video"));
        Assert.Empty(LoadSettings().ApplicationRules.Entries);

        AssertSuccess(RunCli("app-rule", "add", "movie", "--process", "mpv", "--only-night-color"));
        var nightColorRule = Assert.Single(LoadSettings().ApplicationRules.Entries);
        Assert.False(nightColorRule.ExcludeAutomaticEffects);
        Assert.True(nightColorRule.ExcludeNightColor);

        AssertSuccess(RunCli("app-rule", "remove", "movie"));
        Assert.Empty(LoadSettings().ApplicationRules.Entries);
    }

    [Fact]
    public void ScheduleAndStartupRestoreCommands_UpdateSettings()
    {
        AssertSuccess(RunCli("schedule", "list"));
        AssertSuccess(RunCli("schedule", "night", "22:00", "07:00", "24", "warm", "18"));
        Assert.Contains(LoadSettings().Schedules.Entries, schedule => schedule.Id == "night");

        AssertSuccess(RunCli("schedule", "set", "work", "weekdays", "09:00", "17:00", "18", "neutral", "8", "Work Hours"));
        Assert.Contains(LoadSettings().Schedules.Entries, schedule => schedule.Id == "work" && schedule.Name == "Work Hours");

        AssertSuccess(RunCli("schedule", "hardware", "work", @"\\.\DISPLAY2", "0", "70", "--fallback-software"));
        var work = LoadSettings().Schedules.Entries.Single(schedule => schedule.Id == "work");
        Assert.True(work.HardwareBrightness.Enabled);
        Assert.Equal(70, work.HardwareBrightness.Brightness);

        AssertSuccess(RunCli("schedule", "disable-entry", "work"));
        Assert.False(LoadSettings().Schedules.Entries.Single(schedule => schedule.Id == "work").Enabled);

        AssertSuccess(RunCli("schedule", "enable-entry", "work"));
        Assert.True(LoadSettings().Schedules.Entries.Single(schedule => schedule.Id == "work").Enabled);

        AssertSuccess(RunCli("schedule", "remove", "work"));
        Assert.DoesNotContain(LoadSettings().Schedules.Entries, schedule => schedule.Id == "work");

        AssertSuccess(RunCli("startup-restore", "status"));
        AssertSuccess(RunCli("startup-restore", "disable"));
        Assert.False(LoadSettings().StartupRestoration.Enabled);

        AssertSuccess(RunCli("startup-restore", "enable"));
        Assert.True(LoadSettings().StartupRestoration.Enabled);

        AssertSuccess(RunCli("startup-restore", "hardware", @"\\.\DISPLAY2", "0", "70", "--fallback-software"));
        Assert.True(LoadSettings().StartupRestoration.HardwareBrightness.Enabled);

        AssertSuccess(RunCli("startup-restore", "hardware", "off"));
        Assert.False(LoadSettings().StartupRestoration.HardwareBrightness.Enabled);
    }

    public void Dispose()
    {
        if (Directory.Exists(_settingsRoot))
        {
            Directory.Delete(_settingsRoot, recursive: true);
        }
    }

    private CliResult RunCli(params string[] arguments) => _runner.Run(arguments);

    private LumaBrakeSettings LoadSettings() => new SettingsStore(rootDirectory: _settingsRoot).Load();

    private static void AssertSuccess(CliResult result) => CliTestRunner.AssertSuccess(result);
}
