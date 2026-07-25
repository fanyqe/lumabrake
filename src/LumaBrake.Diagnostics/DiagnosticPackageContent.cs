using LumaBrake.Domain.Monitors;

namespace LumaBrake.Diagnostics;

public sealed record DiagnosticPackageContent(
    object Settings,
    IReadOnlyList<MonitorDescriptor> Monitors,
    string? MonitorEnumerationError = null,
    PipelineSnapshot? PipelineSnapshot = null);
