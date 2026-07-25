using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Hardware;
using LumaBrake.Persistence;

namespace LumaBrake.MonitorControl;

public sealed class RuntimeHardwareBrightnessCoordinator
{
    private readonly IHardwareBrightnessWriter _writer;
    private readonly HardwareBrightnessStateStore _stateStore;
    private string? _lastAppliedTargetSignature;

    public RuntimeHardwareBrightnessCoordinator(IHardwareBrightnessWriter writer, HardwareBrightnessStateStore stateStore)
    {
        _writer = writer;
        _stateStore = stateStore;
    }

    public void Seed(LumaBrakeSettings settings, DateTimeOffset localTimestamp)
    {
        _lastAppliedTargetSignature = TargetSignature(EffectiveTarget(settings, localTimestamp));
    }

    public RuntimeHardwareBrightnessApplyResult ApplyIfChanged(LumaBrakeSettings settings, DateTimeOffset localTimestamp)
    {
        if (!settings.Enabled)
        {
            _lastAppliedTargetSignature = null;
            return new(settings, TargetConfigured: false, TargetChanged: false, NativeWriteAttemptRecorded: false, SoftwareFallbackApplied: false, []);
        }

        var options = settings.HardwareBrightness.Clamp();
        if (!options.Enabled || !options.AllowWrites)
        {
            _lastAppliedTargetSignature = null;
            return new(settings, TargetConfigured: false, TargetChanged: false, NativeWriteAttemptRecorded: false, SoftwareFallbackApplied: false, []);
        }

        var target = EffectiveTarget(settings, localTimestamp);
        if (!target.Enabled)
        {
            _lastAppliedTargetSignature = null;
            return new(settings, TargetConfigured: false, TargetChanged: false, NativeWriteAttemptRecorded: false, SoftwareFallbackApplied: false, []);
        }

        var targetSignature = TargetSignature(target);
        if (string.Equals(_lastAppliedTargetSignature, targetSignature, StringComparison.Ordinal))
        {
            return new(settings, TargetConfigured: true, TargetChanged: false, NativeWriteAttemptRecorded: false, SoftwareFallbackApplied: false, []);
        }

        var targetKey = StartupHardwareBrightnessRestorer.TargetKey(target.DisplayDeviceName, target.PhysicalMonitorIndex);
        var lastWriteAt = _stateStore.Load().LastWriteAttemptAt(targetKey);
        var results = _writer.SetBrightness(
                target.DisplayDeviceName,
                target.PhysicalMonitorIndex,
                target.Brightness,
                options,
                localTimestamp,
                lastWriteAt)
            .ToArray();

        var nativeWriteAttempted = results.Any(StartupHardwareBrightnessRestorer.ResultAttemptedNativeWrite);
        if (nativeWriteAttempted)
        {
            _stateStore.RecordWriteAttempt(targetKey, localTimestamp);
        }

        var failed = results.Length == 0 || results.Any(result => !result.Succeeded);
        if (!failed)
        {
            _lastAppliedTargetSignature = targetSignature;
            return new(settings, TargetConfigured: true, TargetChanged: true, nativeWriteAttempted, SoftwareFallbackApplied: false, results);
        }

        if (!target.UseSoftwareFallback)
        {
            _lastAppliedTargetSignature = targetSignature;
            return new(settings, TargetConfigured: true, TargetChanged: true, nativeWriteAttempted, SoftwareFallbackApplied: false, results);
        }

        var fallbackOpacity = HardwareBrightnessSoftwareFallback.ManualDimmingOpacityForBrightness(target.Brightness);
        var fallbackSettings = settings with
        {
            MonitorManualDimming = settings.MonitorManualDimming.Upsert(new MonitorManualDimmingOverride
            {
                MonitorId = target.DisplayDeviceName,
                Opacity = fallbackOpacity
            })
        };

        _lastAppliedTargetSignature = targetSignature;
        return new(fallbackSettings, TargetConfigured: true, TargetChanged: true, nativeWriteAttempted, SoftwareFallbackApplied: true, results);
    }

    private static HardwareBrightnessTarget EffectiveTarget(LumaBrakeSettings settings, DateTimeOffset localTimestamp)
    {
        var activeScheduleTarget = settings.Schedules.ActiveAt(localTimestamp)?.Clamp().HardwareBrightness.Clamp();
        if (activeScheduleTarget is { Enabled: true })
        {
            return activeScheduleTarget;
        }

        return settings.StartupRestoration.HardwareBrightness.Clamp();
    }

    private static string? TargetSignature(HardwareBrightnessTarget target) =>
        target.Enabled
            ? string.Join(
                "|",
                target.DisplayDeviceName.Trim().ToUpperInvariant(),
                Math.Max(target.PhysicalMonitorIndex, 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Math.Clamp(target.Brightness, 0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture),
                target.UseSoftwareFallback ? "fallback" : "native")
            : null;
}
