using System.IO.Compression;
using System.Text.Json;
using LumaBrake.Diagnostics;
using LumaBrake.Domain.Monitors;

namespace LumaBrake.Diagnostics.Tests;

public sealed class DiagnosticPackageExporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lumabrake-diagnostics-{Guid.NewGuid():N}");

    [Fact]
    public void Export_WritesPackageWithoutScreenContent()
    {
        var outputPath = Path.Combine(_root, "diagnostics.zip");
        var exportedPath = new DiagnosticPackageExporter().Export(
            new DiagnosticPackageContent(
                new { enabled = true, userMaximumOpacity = 0.64 },
                [
                    new MonitorDescriptor(
                        "display-1",
                        @"\\.\DISPLAY1",
                        "Primary",
                        IsPrimary: true,
                        IsActive: true,
                        new MonitorBounds(0, 0, 160, 90),
                        new MonitorBounds(0, 0, 160, 90))
                ]),
            outputPath);

        using var archive = ZipFile.OpenRead(exportedPath);
        var entries = archive.Entries.Select(entry => entry.FullName).ToArray();

        Assert.Contains("manifest.json", entries);
        Assert.Contains("settings.json", entries);
        Assert.Contains("monitors.json", entries);
        Assert.Contains("privacy.txt", entries);
        Assert.DoesNotContain(entries, entry => entry.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, entry => entry.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase));

        using var manifestStream = archive.GetEntry("manifest.json")!.Open();
        using var manifest = JsonDocument.Parse(manifestStream);
        Assert.False(manifest.RootElement.GetProperty("includesScreenContent").GetBoolean());

        using var privacyStream = archive.GetEntry("privacy.txt")!.Open();
        using var reader = new StreamReader(privacyStream);
        var privacy = reader.ReadToEnd();
        Assert.Contains("does not contain screenshots", privacy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Export_OverwritesExistingPackage()
    {
        var outputPath = Path.Combine(_root, "diagnostics.zip");
        Directory.CreateDirectory(_root);
        File.WriteAllText(outputPath, "stale");

        var exportedPath = new DiagnosticPackageExporter().Export(
            new DiagnosticPackageContent(new { enabled = false }, []),
            outputPath);

        using var archive = ZipFile.OpenRead(exportedPath);
        Assert.Contains(archive.Entries, entry => entry.FullName == "manifest.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
