namespace LumaBrake.Persistence;

public sealed record HardwareBrightnessState
{
    public Dictionary<string, DateTimeOffset> LastWriteAttempts { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset? LastWriteAttemptAt(string targetKey)
    {
        foreach (var entry in LastWriteAttempts)
        {
            if (string.Equals(entry.Key, targetKey, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }

        return null;
    }

    public HardwareBrightnessState WithWriteAttempt(string targetKey, DateTimeOffset attemptedAt)
    {
        var attempts = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in LastWriteAttempts)
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
