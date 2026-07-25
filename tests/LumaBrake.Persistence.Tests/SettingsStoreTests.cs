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
    }

    [Fact]
    public void Save_And_Load_RoundTripsSettings()
    {
        var store = new SettingsStore(rootDirectory: _root);
        store.Save(new LumaBrakeSettings { Enabled = false, UserMaximumOpacity = 0.25 });

        var loaded = store.Load();

        Assert.False(loaded.Enabled);
        Assert.Equal(0.25, loaded.UserMaximumOpacity, precision: 12);
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

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
