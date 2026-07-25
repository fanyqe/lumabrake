using System.Text.Json;

namespace LumaBrake.Persistence;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public string RootDirectory { get; }

    public SettingsStore(string appName = "LumaBrake", string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ??
            Environment.GetEnvironmentVariable("LUMABRAKE_SETTINGS_ROOT") ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                appName);
        Directory.CreateDirectory(RootDirectory);
        _settingsPath = Path.Combine(RootDirectory, "settings.json");
    }

    public LumaBrakeSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return SettingsMigration.Apply(new LumaBrakeSettings());
        }

        try
        {
            using var stream = File.OpenRead(_settingsPath);
            var settings = JsonSerializer.Deserialize<LumaBrakeSettings>(stream, SerializerOptions) ?? new LumaBrakeSettings();
            return SettingsMigration.Apply(settings);
        }
        catch (JsonException)
        {
            QuarantineCorruptSettings();
            return SettingsMigration.Apply(new LumaBrakeSettings());
        }
    }

    public void Save(LumaBrakeSettings settings)
    {
        JsonFilePersistence.Save(_settingsPath, SettingsMigration.Apply(settings), SerializerOptions);
    }

    private void QuarantineCorruptSettings()
    {
        JsonFilePersistence.Quarantine(_settingsPath);
    }
}
