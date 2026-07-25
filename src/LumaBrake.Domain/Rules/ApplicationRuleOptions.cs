namespace LumaBrake.Domain.Rules;

public sealed record ApplicationRuleOptions
{
    public bool Enabled { get; init; } = true;

    public ApplicationRule[] Entries { get; init; } = [];

    public ApplicationRuleOptions Clamp()
    {
        var entries = (Entries ?? [])
            .Select(rule => rule.Clamp())
            .Where(rule =>
                !string.IsNullOrWhiteSpace(rule.Id) &&
                (!string.IsNullOrWhiteSpace(rule.ProcessName) ||
                    !string.IsNullOrWhiteSpace(rule.WindowTitleContains)))
            .GroupBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();

        return this with { Entries = entries };
    }

    public bool ExcludesAutomaticEffects(ForegroundApplicationInfo? application)
    {
        var options = Clamp();
        return options.Enabled &&
            options.Entries.Any(rule => rule.IsMatch(application));
    }

    public bool ExcludesNightColor(ForegroundApplicationInfo? application)
    {
        var options = Clamp();
        return options.Enabled &&
            options.Entries.Any(rule => rule.IsNightColorMatch(application));
    }

    public ApplicationRule? Find(string? id)
    {
        var normalizedId = NormalizeId(id);
        return normalizedId is null
            ? null
            : Clamp().Entries.FirstOrDefault(entry => string.Equals(entry.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
    }

    public ApplicationRuleOptions Upsert(ApplicationRule rule)
    {
        var incoming = rule.Clamp();
        if (string.IsNullOrWhiteSpace(incoming.Id) ||
            (string.IsNullOrWhiteSpace(incoming.ProcessName) &&
                string.IsNullOrWhiteSpace(incoming.WindowTitleContains)))
        {
            return Clamp();
        }

        var entries = Clamp().Entries.ToList();
        var existingIndex = entries.FindIndex(entry =>
            string.Equals(entry.Id, incoming.Id, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            entries[existingIndex] = incoming;
        }
        else
        {
            entries.Add(incoming);
        }

        return this with { Entries = entries.ToArray() };
    }

    public ApplicationRuleOptions Remove(string? id)
    {
        var normalizedId = NormalizeId(id);
        return normalizedId is null
            ? Clamp()
            : this with
            {
                Entries = Clamp().Entries
                    .Where(entry => !string.Equals(entry.Id, normalizedId, StringComparison.OrdinalIgnoreCase))
                    .ToArray()
            };
    }

    public ApplicationRuleOptions SetEntryEnabled(string? id, bool enabled)
    {
        var normalizedId = NormalizeId(id);
        return normalizedId is null
            ? Clamp()
            : this with
            {
                Entries = Clamp().Entries
                    .Select(entry => string.Equals(entry.Id, normalizedId, StringComparison.OrdinalIgnoreCase)
                        ? entry with { Enabled = enabled }
                        : entry)
                    .ToArray()
            };
    }

    private static string? NormalizeId(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : id.Trim();
}
