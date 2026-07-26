namespace LumaBrake.Domain.Schedules;

public sealed record ScheduleOptions
{
    public bool Enabled { get; init; }

    public ScheduledEffect[] Entries { get; init; } = [];

    public ScheduledEffect? ActiveAt(DateTimeOffset localTimestamp)
    {
        if (!Enabled)
        {
            return null;
        }

        return Clamp().Entries
            .FirstOrDefault(entry => entry.IsActiveAt(localTimestamp));
    }

    public ScheduleOptions Clamp()
    {
        var entries = new List<ScheduledEffect>();
        foreach (var entry in Entries.Select(entry => entry.Clamp()))
        {
            var existingIndex = entries.FindIndex(current =>
                string.Equals(current.Id, entry.Id, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                entries[existingIndex] = entry;
            }
            else
            {
                entries.Add(entry);
            }
        }

        return this with { Entries = entries.ToArray() };
    }

    public ScheduledEffect? Find(string? id)
    {
        var normalizedId = NormalizeId(id);
        return normalizedId is null
            ? null
            : Clamp().Entries.FirstOrDefault(entry => string.Equals(entry.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
    }

    public ScheduleOptions Upsert(ScheduledEffect schedule)
    {
        var incoming = schedule.Clamp();
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

    public ScheduleOptions Remove(string? id)
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

    public ScheduleOptions SetEntryEnabled(string? id, bool enabled)
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
