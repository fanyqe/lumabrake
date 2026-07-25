using System.IO;
using LumaBrake.Diagnostics;
using LumaBrake.Domain.Monitors;
using LumaBrake.Persistence;

namespace LumaBrake.App;

public sealed class DashboardDiagnosticsExporter
{
    private readonly DiagnosticPackageExporter _exporter = new();

    public string Export(
        LumaBrakeSettings settings,
        IReadOnlyList<MonitorDescriptor> monitors,
        string? monitorEnumerationError = null,
        PipelineSnapshot? pipelineSnapshot = null)
    {
        var outputPath = Path.Combine(DefaultOutputDirectory(), $"lumabrake-diagnostics-{DateTimeOffset.Now:yyyyMMddHHmmss}.zip");
        return _exporter.Export(
            new DiagnosticPackageContent(settings, monitors, monitorEnumerationError, pipelineSnapshot),
            outputPath);
    }

    private static string DefaultOutputDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents))
        {
            return Path.Combine(documents, "LumaBrake");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LumaBrake",
            "Diagnostics");
    }
}
