using LumaBrake.Persistence;

namespace LumaBrake.MonitorControl;

public sealed record StartupHardwareBrightnessRestoreResult(
    LumaBrakeSettings Settings,
    bool TargetConfigured,
    bool NativeWriteAttemptRecorded,
    bool SoftwareFallbackApplied,
    IReadOnlyList<HardwareBrightnessWriteResult> WriteResults);
