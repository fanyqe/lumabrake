using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace LumaBrake.Diagnostics;

public sealed class DiagnosticPackageExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
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
            includesScreenContent = false
        });
        AddJson(archive, "settings.json", content.Settings);
        AddJson(archive, "monitors.json", new
        {
            error = content.MonitorEnumerationError,
            monitors = content.Monitors
        });
        AddText(
            archive,
            "privacy.txt",
            "This package contains settings, monitor descriptors, and runtime environment metadata. It does not contain screenshots, raw frames, recordings, OCR output, or screen content.");

        return fullPath;
    }

    private static void AddJson(ZipArchive archive, string entryName, object value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        AddText(archive, entryName, json);
    }

    private static void AddText(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}
