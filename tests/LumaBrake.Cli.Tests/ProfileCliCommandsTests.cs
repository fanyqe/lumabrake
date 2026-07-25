using LumaBrake.Domain.Color;
using LumaBrake.Persistence;

namespace LumaBrake.Cli.Tests;

public sealed class ProfileCliCommandsTests : IDisposable
{
    private readonly string _settingsRoot = Path.Combine(Path.GetTempPath(), $"lumabrake-cli-{Guid.NewGuid():N}");
    private readonly CliTestRunner _runner;

    public ProfileCliCommandsTests()
    {
        _runner = new CliTestRunner(_settingsRoot);
    }

    [Fact]
    public void ProfileImportExport_RoundTripsThroughCli()
    {
        var exportPath = Path.Combine(_settingsRoot, "exports", "evening.json");

        AssertSuccess(RunCli("dim", "25"));
        AssertSuccess(RunCli("tint", "warm", "10"));
        AssertSuccess(RunCli("night-color", "manual", "3300", "80"));
        AssertSuccess(RunCli("focus", "enable"));
        AssertSuccess(RunCli("focus", "opacity", "20"));
        AssertSuccess(RunCli("profile", "save", "evening", "Evening Work"));

        var exportResult = RunCli("profile", "export", "evening", exportPath);
        AssertSuccess(exportResult);
        Assert.Contains("Profile exported: evening", exportResult.StandardOutput, StringComparison.Ordinal);
        Assert.True(File.Exists(exportPath));

        AssertSuccess(RunCli("profile", "remove", "evening"));
        Assert.Empty(new SettingsStore(rootDirectory: _settingsRoot).Load().Profiles.Entries);

        var importResult = RunCli("profile", "import", exportPath);
        AssertSuccess(importResult);
        Assert.Contains("Profile imported: evening", importResult.StandardOutput, StringComparison.Ordinal);

        var profile = Assert.Single(new SettingsStore(rootDirectory: _settingsRoot).Load().Profiles.Entries);
        Assert.Equal("evening", profile.Id);
        Assert.Equal("Evening Work", profile.Name);
        Assert.Equal(0.25, profile.ManualDimmingOpacity, precision: 12);
        Assert.True(profile.Tint.Enabled);
        Assert.Equal(0.10, profile.Tint.Opacity, precision: 12);
        Assert.True(profile.NightColor.Enabled);
        Assert.Equal(NightColorScheduleMode.Manual, profile.NightColor.ScheduleMode);
        Assert.Equal(3300, profile.NightColor.ManualTemperatureKelvin);
        Assert.Equal(0.80, profile.NightColor.Brightness, precision: 12);
        Assert.True(profile.FocusDimming.Enabled);
        Assert.Equal(0.20, profile.FocusDimming.Opacity, precision: 12);
    }

    [Fact]
    public void ProfileImport_ReplacesExistingProfileCaseInsensitively()
    {
        Directory.CreateDirectory(_settingsRoot);
        var firstPath = Path.Combine(_settingsRoot, "first.json");
        var secondPath = Path.Combine(_settingsRoot, "second.json");
        File.WriteAllText(
            firstPath,
            """
            {
              "id": "EVENING",
              "name": "Old",
              "manualDimmingOpacity": 0.1
            }
            """);
        File.WriteAllText(
            secondPath,
            """
            {
              "id": "evening",
              "name": "New",
              "manualDimmingOpacity": 0.3
            }
            """);

        AssertSuccess(RunCli("profile", "import", firstPath));
        AssertSuccess(RunCli("profile", "import", secondPath));

        var profile = Assert.Single(new SettingsStore(rootDirectory: _settingsRoot).Load().Profiles.Entries);
        Assert.Equal("evening", profile.Id);
        Assert.Equal("New", profile.Name);
        Assert.Equal(0.30, profile.ManualDimmingOpacity, precision: 12);
    }

    [Fact]
    public void ProfileImport_RejectsInvalidJsonWithoutChangingSettings()
    {
        AssertSuccess(RunCli("dim", "15"));
        AssertSuccess(RunCli("profile", "save", "safe", "Safe"));
        var before = File.ReadAllText(Path.Combine(_settingsRoot, "settings.json"));

        var invalidPath = Path.Combine(_settingsRoot, "invalid.json");
        File.WriteAllText(invalidPath, "{");

        var result = RunCli("profile", "import", invalidPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("invalid JSON", result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllText(Path.Combine(_settingsRoot, "settings.json")));
    }

    [Fact]
    public void ProfileExport_RejectsMissingProfileWithoutCreatingFile()
    {
        var exportPath = Path.Combine(_settingsRoot, "missing.json");

        var result = RunCli("profile", "export", "missing", exportPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Profile not found: missing", result.StandardError, StringComparison.Ordinal);
        Assert.False(File.Exists(exportPath));
    }

    [Theory]
    [InlineData("import")]
    [InlineData("export")]
    public void ProfileFileCommands_RejectMissingArguments(string command)
    {
        var result = RunCli("profile", command);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Usage: lumabrake profile", result.StandardError, StringComparison.Ordinal);
    }

    private CliResult RunCli(params string[] arguments)
    {
        return _runner.Run(arguments);
    }

    private static void AssertSuccess(CliResult result)
    {
        CliTestRunner.AssertSuccess(result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_settingsRoot))
        {
            Directory.Delete(_settingsRoot, recursive: true);
        }
    }
}
