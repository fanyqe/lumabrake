namespace LumaBrake.Persistence;

public sealed record HardwareBrightnessState
{
    public Dictionary<string, DateTimeOffset> LastWriteAttempts { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset? LastWriteAttemptAt(string? targetKey)
    {
        if (string.IsNullOrWhiteSpace(targetKey))
        {
            return null;
        }

        var normalizedTargetKey = targetKey.Trim();
        foreach (var entry in LastWriteAttempts ?? new Dictionary<string, DateTimeOffset>())
        {
            if (string.Equals(entry.Key.Trim(), normalizedTargetKey, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }

        return null;
    }

    public HardwareBrightnessState WithWriteAttempt(string targetKey, DateTimeOffset attemptedAt)
    {
        if (string.IsNullOrWhiteSpace(targetKey))
        {
            throw new ArgumentException("Hardware brightness target key is required.", nameof(targetKey));
        }

        var attempts = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in LastWriteAttempts ?? new Dictionary<string, DateTimeOffset>())
        {
            if (!string.IsNullOrWhiteSpace(entry.Key))
            {
                attempts[entry.Key.Trim()] = entry.Value;
            }
        }

        attempts[targetKey.Trim()] = attemptedAt;
        return this with { LastWriteAttempts = attempts };
    }
}
