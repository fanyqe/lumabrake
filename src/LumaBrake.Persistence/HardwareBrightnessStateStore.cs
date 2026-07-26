using System.Text.Json;

namespace LumaBrake.Persistence;

public sealed class HardwareBrightnessStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _statePath;

    public HardwareBrightnessStateStore(string appName = "LumaBrake", string? rootDirectory = null)
    {
        var root = rootDirectory ??
            Environment.GetEnvironmentVariable("LUMABRAKE_SETTINGS_ROOT") ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                appName);
        Directory.CreateDirectory(root);
        _statePath = Path.Combine(root, "hardware-brightness-state.json");
    }

    public HardwareBrightnessState Load()
    {
        if (!File.Exists(_statePath))
        {
            return new HardwareBrightnessState();
        }

        try
        {
            using var stream = File.OpenRead(_statePath);
            return JsonSerializer.Deserialize<HardwareBrightnessState>(stream, SerializerOptions) ?? new HardwareBrightnessState();
        }
        catch (JsonException)
        {
            QuarantineCorruptState();
            return new HardwareBrightnessState();
        }
    }

    public void Save(HardwareBrightnessState state)
    {
        var tempPath = $"{_statePath}.tmp";
        using (var stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, state, SerializerOptions);
        }

        File.Move(tempPath, _statePath, overwrite: true);
    }

    public void RecordWriteAttempt(string targetKey, DateTimeOffset attemptedAt) =>
        Save(Load().WithWriteAttempt(targetKey, attemptedAt));

    private void QuarantineCorruptState()
    {
        var backupPath = $"{_statePath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        File.Move(_statePath, backupPath, overwrite: true);
    }
}
