using LumaBrake.Persistence;

namespace LumaBrake.MonitorControl;

public sealed record RuntimeHardwareBrightnessApplyResult(
    LumaBrakeSettings Settings,
    bool TargetConfigured,
    bool TargetChanged,
    bool NativeWriteAttemptRecorded,
    bool SoftwareFallbackApplied,
    IReadOnlyList<HardwareBrightnessWriteResult> WriteResults);
