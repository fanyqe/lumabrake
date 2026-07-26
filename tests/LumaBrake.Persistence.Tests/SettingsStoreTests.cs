using LumaBrake.Persistence;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Schedules;

namespace LumaBrake.Persistence.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lumabrake-settings-{Guid.NewGuid():N}");

    [Fact]
    public void Load_ReturnsDefaultsWhenSettingsFileIsMissing()
    {
        var settings = new SettingsStore(rootDirectory: _root).Load();

        Assert.True(settings.Enabled);
        Assert.Equal(0.64, settings.UserMaximumOpacity, precision: 12);
        Assert.Equal(0, settings.ManualDimmingOpacity, precision: 12);
        Assert.False(settings.Tint.Enabled);
        Assert.False(settings.Schedules.Enabled);
        Assert.Empty(settings.Schedules.Entries);
        Assert.True(settings.StartupRestoration.Enabled);
        Assert.True(settings.StartupRestoration.ApplySchedules);
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
            }
        });

        var loaded = store.Load();

        Assert.False(loaded.Enabled);
        Assert.Equal(0.25, loaded.UserMaximumOpacity, precision: 12);
        Assert.Equal(0.18, loaded.ManualDimmingOpacity, precision: 12);
        Assert.True(loaded.Schedules.Enabled);
        var schedule = Assert.Single(loaded.Schedules.Entries);
        Assert.Equal("night", schedule.Id);
        Assert.Equal(0.22, schedule.ManualDimmingOpacity, precision: 12);
        Assert.True(schedule.Tint.Enabled);
    }

    [Fact]
    public void Load_BackfillsScheduleDefaultsForOlderSettingsJson()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "settings.json"),
            """
            {
              "enabled": false,
              "userMaximumOpacity": 0.40,
              "manualDimmingOpacity": 0.12
            }
            """);

        var loaded = new SettingsStore(rootDirectory: _root).Load();

        Assert.False(loaded.Enabled);
        Assert.Equal(0.40, loaded.UserMaximumOpacity, precision: 12);
        Assert.Equal(0.12, loaded.ManualDimmingOpacity, precision: 12);
        Assert.False(loaded.Schedules.Enabled);
        Assert.Empty(loaded.Schedules.Entries);
        Assert.True(loaded.StartupRestoration.Enabled);
    }

    [Fact]
    public void StartupRestoration_AppliesActiveScheduleTintWithoutDuplicatingDimming()
    {
        var settings = new LumaBrakeSettings
        {
            ManualDimmingOpacity = 0.05,
            Tint = new TintOptions { Enabled = false },
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
    }

    [Fact]
    public void StartupRestoration_KeepsPersistedEffectsWhenNoScheduleIsActive()
    {
        var settings = new LumaBrakeSettings
        {
            ManualDimmingOpacity = 0.14,
            Tint = new TintOptions { Enabled = true, Opacity = 0.08 },
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
    }

    [Fact]
    public void StartupRestoration_DisabledClearsSoftwareEffectsForStartup()
    {
        var settings = new LumaBrakeSettings
        {
            ManualDimmingOpacity = 0.33,
            Tint = new TintOptions { Enabled = true, Opacity = 0.20 },
            StartupRestoration = new StartupRestorationOptions { Enabled = false }
        };

        var restored = StartupRestorationPolicy.Apply(
            settings,
            new DateTimeOffset(2026, 7, 26, 23, 0, 0, TimeSpan.FromHours(3)));

        Assert.Equal(0, restored.ManualDimmingOpacity, precision: 12);
        Assert.False(restored.Tint.Enabled);
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
    public void SafeMode_DisablesEffectsAndClearsManualDimming()
    {
        var settings = new LumaBrakeSettings
        {
            Enabled = true,
            UserMaximumOpacity = 0.64,
            ManualDimmingOpacity = 0.35
        };

        var safe = SafeModeSettings.Apply(settings);

        Assert.False(safe.Enabled);
        Assert.Equal(0.25, safe.UserMaximumOpacity, precision: 12);
        Assert.Equal(0, safe.ManualDimmingOpacity, precision: 12);
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

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
