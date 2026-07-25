using System.IO;
using System.Text.Json;

namespace LumaBrake.Platform.Windows;

internal sealed class GammaRampRecoveryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _recoveryPath;

    public GammaRampRecoveryStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Recovery root directory is required.", nameof(rootDirectory));
        }

        var fullRoot = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(fullRoot);
        _recoveryPath = Path.Combine(fullRoot, "night-color-gamma-recovery.json");
    }

    public IReadOnlyList<GammaRampRecoveryEntry> Load()
    {
        if (!File.Exists(_recoveryPath))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(_recoveryPath);
            var file = JsonSerializer.Deserialize<GammaRampRecoveryFile>(stream, SerializerOptions);
            return Normalize(file?.Entries);
        }
        catch (JsonException)
        {
            TryDelete();
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    public bool TryUpsert(string deviceName, ushort[] values)
    {
        if (string.IsNullOrWhiteSpace(deviceName) || values.Length != GammaRamp.ValueCount)
        {
            return false;
        }

        try
        {
            var entries = Load()
                .Where(entry => !string.Equals(entry.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                .Append(new GammaRampRecoveryEntry(deviceName.Trim(), values.ToArray()))
                .OrderBy(entry => entry.DeviceName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Save(entries);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Remove(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return;
        }

        try
        {
            var entries = Load()
                .Where(entry => !string.Equals(entry.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Save(entries);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void Save(IReadOnlyList<GammaRampRecoveryEntry> entries)
    {
        if (entries.Count == 0)
        {
            TryDelete();
            return;
        }

        var directory = Path.GetDirectoryName(_recoveryPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _recoveryPath + ".tmp";
        using (var stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, new GammaRampRecoveryFile(entries.ToArray()), SerializerOptions);
        }

        File.Move(tempPath, _recoveryPath, overwrite: true);
    }

    private void TryDelete()
    {
        try
        {
            if (File.Exists(_recoveryPath))
            {
                File.Delete(_recoveryPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static GammaRampRecoveryEntry[] Normalize(IEnumerable<GammaRampRecoveryEntry>? entries) =>
        (entries ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry.DeviceName) && entry.Values.Length == GammaRamp.ValueCount)
            .Select(entry => new GammaRampRecoveryEntry(entry.DeviceName.Trim(), entry.Values.ToArray()))
            .DistinctBy(entry => entry.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private sealed record GammaRampRecoveryFile(GammaRampRecoveryEntry[] Entries);
}

internal sealed record GammaRampRecoveryEntry(string DeviceName, ushort[] Values);
