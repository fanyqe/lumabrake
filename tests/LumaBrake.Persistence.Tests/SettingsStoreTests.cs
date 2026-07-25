using LumaBrake.Persistence;

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
    }

    [Fact]
    public void Save_And_Load_RoundTripsSettings()
    {
        var store = new SettingsStore(rootDirectory: _root);
        store.Save(new LumaBrakeSettings { Enabled = false, UserMaximumOpacity = 0.25, ManualDimmingOpacity = 0.18 });

        var loaded = store.Load();

        Assert.False(loaded.Enabled);
        Assert.Equal(0.25, loaded.UserMaximumOpacity, precision: 12);
        Assert.Equal(0.18, loaded.ManualDimmingOpacity, precision: 12);
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
