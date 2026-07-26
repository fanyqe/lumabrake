namespace LumaBrake.Domain.Hardware;

public sealed class HardwareBrightnessPolicy
{
    private readonly HardwareBrightnessOptions _options;

    public HardwareBrightnessPolicy(HardwareBrightnessOptions options)
    {
        _options = options.Clamp();
    }

    public bool CanRead(string displayDeviceName, DateTimeOffset now, DateTimeOffset? lastReadAt) =>
        _options.Enabled &&
        !IsBlacklisted(displayDeviceName) &&
        IsIntervalElapsed(now, lastReadAt, _options.MinimumReadInterval);

    public bool CanWrite(string displayDeviceName, DateTimeOffset now, DateTimeOffset? lastWriteAt) =>
        _options.Enabled &&
        _options.AllowWrites &&
        !IsBlacklisted(displayDeviceName) &&
        IsIntervalElapsed(now, lastWriteAt, _options.MinimumWriteInterval);

    public bool IsBlacklisted(string displayDeviceName) =>
        _options.BlacklistedDisplayDeviceNames.Contains(displayDeviceName, StringComparer.OrdinalIgnoreCase);

    private static bool IsIntervalElapsed(DateTimeOffset now, DateTimeOffset? lastAttemptAt, TimeSpan minimumInterval) =>
        lastAttemptAt is null || now - lastAttemptAt.Value >= minimumInterval;
}
