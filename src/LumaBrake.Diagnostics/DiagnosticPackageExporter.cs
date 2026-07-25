using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace LumaBrake.Diagnostics;

public sealed class DiagnosticPackageExporter
{
    private const string RedactedPath = "[redacted-path]";

    private static readonly Regex DrivePathPattern = new(
        @"(?<![A-Za-z0-9_])[A-Za-z]:[\\/][^\r\n""]+",
        RegexOptions.Compiled);

    private static readonly Regex UncPathPattern = new(
        @"\\\\(?![.?]\\)[^\r\n""]+",
        RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Export(DiagnosticPackageContent content, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path must not be empty.", nameof(outputPath));
        }

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        using var archive = ZipFile.Open(fullPath, ZipArchiveMode.Create);
        AddJson(archive, "manifest.json", new
        {
            app = "LumaBrake",
            createdAt = DateTimeOffset.UtcNow,
            os = Environment.OSVersion.VersionString,
            processArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            includesScreenContent = false,
            pathsRedacted = true,
            includesRuntimeSnapshot = content.PipelineSnapshot is not null
        });
        AddJson(archive, "settings.json", content.Settings);
        AddJson(archive, "monitors.json", new
        {
            error = content.MonitorEnumerationError,
            monitors = content.Monitors
        });
        if (content.PipelineSnapshot is not null)
        {
            AddJson(archive, "pipeline.json", content.PipelineSnapshot);
        }

        AddText(
            archive,
            "privacy.txt",
            "This package contains settings, monitor descriptors, runtime pipeline counters, and runtime environment metadata. Filesystem paths are redacted. It does not contain screenshots, raw frames, recordings, OCR output, or screen content.");

        return fullPath;
    }

    private static void AddJson(ZipArchive archive, string entryName, object value)
    {
        var json = JsonSerializer.Serialize(RedactJson(JsonSerializer.SerializeToNode(value, JsonOptions)), JsonOptions);
        AddText(archive, entryName, json);
    }

    private static void AddText(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static JsonNode? RedactJson(JsonNode? node) =>
        node switch
        {
            JsonObject value => RedactObject(value),
            JsonArray value => RedactArray(value),
            JsonValue value when value.TryGetValue<string>(out var text) => JsonValue.Create(RedactPaths(text)),
            _ => node?.DeepClone()
        };

    private static JsonObject RedactObject(JsonObject value)
    {
        var redacted = new JsonObject();
        foreach (var item in value)
        {
            redacted[item.Key] = RedactJson(item.Value);
        }

        return redacted;
    }

    private static JsonArray RedactArray(JsonArray value)
    {
        var redacted = new JsonArray();
        foreach (var item in value)
        {
            redacted.Add(RedactJson(item));
        }

        return redacted;
    }

    private static string RedactPaths(string value)
    {
        var redacted = DrivePathPattern.Replace(value, RedactedPath);
        return UncPathPattern.Replace(redacted, RedactedPath);
    }
}
