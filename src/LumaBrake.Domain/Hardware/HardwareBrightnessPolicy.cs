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
        WriteBlockReason(displayDeviceName, now, lastWriteAt) is null;

    public string? WriteBlockReason(string displayDeviceName, DateTimeOffset now, DateTimeOffset? lastWriteAt)
    {
        if (!_options.Enabled)
        {
            return "Hardware brightness control is disabled.";
        }

        if (!_options.AllowWrites)
        {
            return "Hardware brightness writes are disabled.";
        }

        if (IsBlacklisted(displayDeviceName))
        {
            return "Hardware brightness is blacklisted for this display device.";
        }

        return IsIntervalElapsed(now, lastWriteAt, _options.MinimumWriteInterval)
            ? null
            : "Hardware brightness was written too recently.";
    }

    public bool IsBlacklisted(string displayDeviceName) =>
        _options.BlacklistedDisplayDeviceNames.Contains(displayDeviceName, StringComparer.OrdinalIgnoreCase);

    private static bool IsIntervalElapsed(DateTimeOffset now, DateTimeOffset? lastAttemptAt, TimeSpan minimumInterval) =>
        lastAttemptAt is null || now - lastAttemptAt.Value >= minimumInterval;
}
