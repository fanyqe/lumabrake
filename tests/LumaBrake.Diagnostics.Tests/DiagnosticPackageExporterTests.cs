using System.IO.Compression;
using System.Text.Json;
using LumaBrake.Diagnostics;
using LumaBrake.Domain.Effects;
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
                    {
                        AdvancedColor = new MonitorAdvancedColorState(
                            IsKnown: true,
                            IsSupported: true,
                            IsEnabled: true,
                            IsWideColorGamutEnabled: false,
                            IsForceDisabled: false,
                            ColorEncoding: "RGB",
                            BitsPerColorChannel: 10)
                    }
                ]),
            outputPath);

        using var archive = ZipFile.OpenRead(exportedPath);
        var entries = archive.Entries.Select(entry => entry.FullName).ToArray();

        Assert.Contains("manifest.json", entries);
        Assert.Contains("settings.json", entries);
        Assert.Contains("monitors.json", entries);
        Assert.Contains("privacy.txt", entries);
        Assert.DoesNotContain("pipeline.json", entries);
        Assert.DoesNotContain(entries, entry => entry.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, entry => entry.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase));

        using var manifestStream = archive.GetEntry("manifest.json")!.Open();
        using var manifest = JsonDocument.Parse(manifestStream);
        Assert.False(manifest.RootElement.GetProperty("includesScreenContent").GetBoolean());
        Assert.True(manifest.RootElement.GetProperty("pathsRedacted").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("includesRuntimeSnapshot").GetBoolean());

        using var privacyStream = archive.GetEntry("privacy.txt")!.Open();
        using var reader = new StreamReader(privacyStream);
        var privacy = reader.ReadToEnd();
        Assert.Contains("does not contain screenshots", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("paths are redacted", privacy, StringComparison.OrdinalIgnoreCase);

        using var monitorsStream = archive.GetEntry("monitors.json")!.Open();
        using var monitors = JsonDocument.Parse(monitorsStream);
        var advancedColor = monitors.RootElement
            .GetProperty("monitors")[0]
            .GetProperty("advancedColor");
        Assert.True(advancedColor.GetProperty("isEnabled").GetBoolean());
        Assert.Equal("RGB", advancedColor.GetProperty("colorEncoding").GetString());
        Assert.Equal(@"\\.\DISPLAY1", monitors.RootElement.GetProperty("monitors")[0].GetProperty("deviceName").GetString());
    }

    [Fact]
    public void Export_WritesRuntimePipelineSnapshotWhenProvided()
    {
        var outputPath = Path.Combine(_root, "diagnostics.zip");
        var snapshot = new PipelineSnapshot(
            DateTimeOffset.UnixEpoch,
            IsEnabled: true,
            AverageLuminance: 0.42,
            Percentile95Luminance: 0.91,
            OverlayOpacity: 0.35,
            ActiveReason: EffectReason.RepetitiveFlash,
            ActiveMonitor: @"\\.\DISPLAY2",
            MonitorCount: 1,
            MonitorStatus: "1 monitor active.",
            TransientFailureCount: 2,
            LastTransientFailureAt: DateTimeOffset.UnixEpoch.AddSeconds(5),
            CaptureAnomalyEvents: 3,
            LastCaptureAnomalyAt: DateTimeOffset.UnixEpoch.AddSeconds(6),
            HasActiveCaptureAnomaly: true,
            CaptureFailureEvents: 1,
            LastCaptureFailureAt: DateTimeOffset.UnixEpoch.AddSeconds(7),
            HasActiveCaptureFailure: true,
            FramesAnalyzed: 240,
            SpikeEvents: 4,
            FlashEvents: 5,
            RedFlashEvents: 1,
            RegionalMaskCount: 2,
            RegionalMaskMaxOpacity: 0.44,
            Monitors:
            [
                new MonitorPipelineSnapshot(
                    "display-2",
                    @"\\.\DISPLAY2",
                    "Secondary HDR",
                    IsPrimary: false,
                    AverageLuminance: 0.39,
                    Percentile95Luminance: 0.88,
                    OverlayOpacity: 0.35,
                    ActiveReason: EffectReason.RepetitiveFlash,
                    FramesAnalyzed: 240,
                    CaptureAnomalyEvents: 3,
                    LastCaptureAnomalyAt: DateTimeOffset.UnixEpoch.AddSeconds(6),
                    IsCaptureAnomalyActive: true,
                    CaptureFailureEvents: 1,
                    LastCaptureFailureAt: DateTimeOffset.UnixEpoch.AddSeconds(7),
                    IsCaptureFailureActive: true,
                    SpikeEvents: 4,
                    FlashEvents: 5,
                    RedFlashEvents: 1,
                    RegionalMaskCount: 2,
                    RegionalMaskMaxOpacity: 0.44,
                    new MonitorAdvancedColorState(
                        IsKnown: true,
                        IsSupported: true,
                        IsEnabled: true,
                        IsWideColorGamutEnabled: false,
                        IsForceDisabled: false,
                        ColorEncoding: "RGB",
                        BitsPerColorChannel: 10))
                {
                    RegionalMasks =
                    [
                        new RegionalEffectMask(
                            EffectReason.RepetitiveFlash,
                            0.44,
                            new EffectRegion(0.10, 0.20, 0.30, 0.40))
                    ]
                }
            ])
        {
            RegionalMasks =
            [
                new RegionalEffectMask(
                    EffectReason.RepetitiveFlash,
                    0.44,
                    new EffectRegion(0.10, 0.20, 0.30, 0.40))
            ]
        };

        var exportedPath = new DiagnosticPackageExporter().Export(
            new DiagnosticPackageContent(new { enabled = true }, [], PipelineSnapshot: snapshot),
            outputPath);

        using var archive = ZipFile.OpenRead(exportedPath);
        var entries = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Contains("pipeline.json", entries);
        Assert.DoesNotContain(entries, entry => entry.EndsWith(".png", StringComparison.OrdinalIgnoreCase));

        using var manifestStream = archive.GetEntry("manifest.json")!.Open();
        using var manifest = JsonDocument.Parse(manifestStream);
        Assert.True(manifest.RootElement.GetProperty("includesRuntimeSnapshot").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("includesScreenContent").GetBoolean());

        using var pipelineStream = archive.GetEntry("pipeline.json")!.Open();
        using var pipeline = JsonDocument.Parse(pipelineStream);
        Assert.Equal("repetitiveFlash", pipeline.RootElement.GetProperty("activeReason").GetString());
        Assert.Equal(240, pipeline.RootElement.GetProperty("framesAnalyzed").GetInt64());
        Assert.True(pipeline.RootElement.GetProperty("hasActiveCaptureAnomaly").GetBoolean());
        Assert.Equal(1, pipeline.RootElement.GetProperty("captureFailureEvents").GetInt64());
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(7), pipeline.RootElement.GetProperty("lastCaptureFailureAt").GetDateTimeOffset());
        Assert.True(pipeline.RootElement.GetProperty("hasActiveCaptureFailure").GetBoolean());

        var monitor = pipeline.RootElement.GetProperty("monitors")[0];
        Assert.Equal("display-2", monitor.GetProperty("stableId").GetString());
        Assert.Equal("repetitiveFlash", monitor.GetProperty("activeReason").GetString());
        Assert.Equal(1, monitor.GetProperty("captureFailureEvents").GetInt64());
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(7), monitor.GetProperty("lastCaptureFailureAt").GetDateTimeOffset());
        Assert.True(monitor.GetProperty("isCaptureFailureActive").GetBoolean());
        Assert.True(monitor.GetProperty("advancedColor").GetProperty("isEnabled").GetBoolean());
        var regionalMask = pipeline.RootElement.GetProperty("regionalMasks")[0];
        Assert.Equal("repetitiveFlash", regionalMask.GetProperty("reason").GetString());
        Assert.Equal(0.44, regionalMask.GetProperty("opacity").GetDouble(), precision: 12);
        Assert.Equal(0.10, regionalMask.GetProperty("region").GetProperty("x").GetDouble(), precision: 12);
        Assert.Equal(0.20, regionalMask.GetProperty("region").GetProperty("y").GetDouble(), precision: 12);
        Assert.Equal(0.30, regionalMask.GetProperty("region").GetProperty("width").GetDouble(), precision: 12);
        Assert.Equal(0.40, regionalMask.GetProperty("region").GetProperty("height").GetDouble(), precision: 12);
        Assert.Equal(0.10, monitor.GetProperty("regionalMasks")[0].GetProperty("region").GetProperty("x").GetDouble(), precision: 12);
    }

    [Fact]
    public void Export_RedactsFilesystemPathsFromJsonPayloads()
    {
        var outputPath = Path.Combine(_root, "diagnostics.zip");
        var localPath = @"C:\Users\alex\AppData\Local\LumaBrake\settings.json";
        var uncPath = @"\\server\share\support\trace.log";

        var exportedPath = new DiagnosticPackageExporter().Export(
            new DiagnosticPackageContent(
                new
                {
                    startupPath = localPath,
                    nested = new[] { $"see {uncPath}" }
                },
                [],
                $"Could not read {localPath}"),
            outputPath);

        using var archive = ZipFile.OpenRead(exportedPath);
        var settingsJson = ReadEntry(archive, "settings.json");
        var monitorsJson = ReadEntry(archive, "monitors.json");

        Assert.Contains("[redacted-path]", settingsJson);
        Assert.Contains("[redacted-path]", monitorsJson);
        Assert.DoesNotContain("alex", settingsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server", settingsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alex", monitorsJson, StringComparison.OrdinalIgnoreCase);
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

    private static string ReadEntry(ZipArchive archive, string entryName)
    {
        using var stream = archive.GetEntry(entryName)!.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
