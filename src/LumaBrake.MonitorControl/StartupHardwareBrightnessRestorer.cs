using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Hardware;
using LumaBrake.Persistence;

namespace LumaBrake.MonitorControl;

public sealed class StartupHardwareBrightnessRestorer
{
    private readonly IHardwareBrightnessWriter _writer;
    private readonly HardwareBrightnessStateStore _stateStore;

    public StartupHardwareBrightnessRestorer(IHardwareBrightnessWriter writer, HardwareBrightnessStateStore stateStore)
    {
        _writer = writer;
        _stateStore = stateStore;
    }

    public StartupHardwareBrightnessRestoreResult Apply(LumaBrakeSettings settings, DateTimeOffset now)
    {
        var target = settings.StartupRestoration.HardwareBrightness.Clamp();
        if (!target.Enabled)
        {
            return new(settings, TargetConfigured: false, NativeWriteAttemptRecorded: false, SoftwareFallbackApplied: false, []);
        }

        var targetKey = TargetKey(target.DisplayDeviceName, target.PhysicalMonitorIndex);
        var lastWriteAt = _stateStore.Load().LastWriteAttemptAt(targetKey);
        var results = _writer.SetBrightness(
                target.DisplayDeviceName,
                target.PhysicalMonitorIndex,
                target.Brightness,
                settings.HardwareBrightness,
                now,
                lastWriteAt)
            .ToArray();

        var nativeWriteAttempted = results.Any(ResultAttemptedNativeWrite);
        if (nativeWriteAttempted)
        {
            _stateStore.RecordWriteAttempt(targetKey, now);
        }

        var failed = results.Length == 0 || results.Any(result => !result.Succeeded);
        if (!failed || !target.UseSoftwareFallback)
        {
            return new(settings, TargetConfigured: true, nativeWriteAttempted, SoftwareFallbackApplied: false, results);
        }

        var fallbackOpacity = HardwareBrightnessSoftwareFallback.ManualDimmingOpacityForBrightness(target.Brightness);
        var fallbackEntry = new MonitorManualDimmingOverride
        {
            MonitorId = target.DisplayDeviceName,
            Opacity = fallbackOpacity
        };
        var fallbackSettings = settings with
        {
            MonitorManualDimming = settings.MonitorManualDimming.Upsert(fallbackEntry)
        };

        return new(fallbackSettings, TargetConfigured: true, nativeWriteAttempted, SoftwareFallbackApplied: true, results);
    }

    public static string TargetKey(string displayDeviceName, int physicalMonitorIndex) =>
        $"{displayDeviceName.Trim()}#{Math.Max(physicalMonitorIndex, 0).ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    internal static bool ResultAttemptedNativeWrite(HardwareBrightnessWriteResult result) =>
        result.Succeeded ||
        result.Error?.StartsWith("Hardware brightness could not be written.", StringComparison.Ordinal) == true;
}
