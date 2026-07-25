using System.Text.Json;

namespace LumaBrake.Persistence;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public SettingsStore(string appName = "LumaBrake", string? rootDirectory = null)
    {
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            appName);
        Directory.CreateDirectory(root);
        _settingsPath = Path.Combine(root, "settings.json");
    }

    public LumaBrakeSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new LumaBrakeSettings();
        }

        try
        {
            using var stream = File.OpenRead(_settingsPath);
            return JsonSerializer.Deserialize<LumaBrakeSettings>(stream, SerializerOptions) ?? new LumaBrakeSettings();
        }
        catch (JsonException)
        {
            QuarantineCorruptSettings();
            return new LumaBrakeSettings();
        }
    }

    public void Save(LumaBrakeSettings settings)
    {
        var tempPath = $"{_settingsPath}.tmp";
        using (var stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, settings, SerializerOptions);
        }

        File.Move(tempPath, _settingsPath, overwrite: true);
    }

    private void QuarantineCorruptSettings()
    {
        var backupPath = $"{_settingsPath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        File.Move(_settingsPath, backupPath, overwrite: true);
    }
}
