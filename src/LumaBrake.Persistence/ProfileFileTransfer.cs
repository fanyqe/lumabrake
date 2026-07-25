using System.Text.Json;
using LumaBrake.Domain.Profiles;

namespace LumaBrake.Persistence;

public static class ProfileFileTransfer
{
    public const long MaximumProfileFileBytes = 64 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string Export(LumaBrakeProfile profile, string outputPath)
    {
        var outputFullPath = ResolveOutputPath(outputPath);
        var directory = Path.GetDirectoryName(outputFullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{outputFullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, profile.Clamp(), SerializerOptions);
            }

            File.Move(tempPath, outputFullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        return outputFullPath;
    }

    public static LumaBrakeProfile Import(string inputPath)
    {
        var inputFullPath = ResolveInputPath(inputPath);
        var fileInfo = new FileInfo(inputFullPath);
        if (fileInfo.Length > MaximumProfileFileBytes)
        {
            throw new InvalidDataException($"Profile file is larger than {MaximumProfileFileBytes} bytes.");
        }

        try
        {
            using var stream = File.OpenRead(inputFullPath);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                throw new InvalidDataException("Profile file must contain one JSON object.");
            }

            if (!TryReadRequiredString(document.RootElement, "id", out var id))
            {
                throw new InvalidDataException("Profile file must contain a non-empty id.");
            }

            var profile = document.RootElement.Deserialize<LumaBrakeProfile>(SerializerOptions);
            if (profile is null)
            {
                throw new InvalidDataException("Profile file could not be read.");
            }

            return (profile with { Id = id }).Clamp();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Profile file contains invalid JSON.", ex);
        }
    }

    private static string ResolveInputPath(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Profile file path is required.", nameof(inputPath));
        }

        var inputFullPath = Path.GetFullPath(inputPath);
        if (!File.Exists(inputFullPath))
        {
            throw new FileNotFoundException("Profile file was not found.", inputFullPath);
        }

        return inputFullPath;
    }

    private static string ResolveOutputPath(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Profile file path is required.", nameof(outputPath));
        }

        return Path.GetFullPath(outputPath);
    }

    private static bool TryReadRequiredString(JsonElement root, string propertyName, out string value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind is JsonValueKind.String)
            {
                value = property.Value.GetString()?.Trim() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            }
        }

        value = string.Empty;
        return false;
    }
}
