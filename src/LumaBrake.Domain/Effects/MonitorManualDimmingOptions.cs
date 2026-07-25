using LumaBrake.Domain.Monitors;

namespace LumaBrake.Domain.Effects;

public sealed record MonitorManualDimmingOptions
{
    public MonitorManualDimmingOverride[] Entries { get; init; } = [];

    public MonitorManualDimmingOptions Clamp()
    {
        var entries = new List<MonitorManualDimmingOverride>();
        foreach (var entry in Entries.Select(entry => entry.Clamp()).Where(entry => !string.IsNullOrWhiteSpace(entry.MonitorId)))
        {
            var existingIndex = entries.FindIndex(current =>
                string.Equals(current.MonitorId, entry.MonitorId, StringComparison.OrdinalIgnoreCase));

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

    public MonitorManualDimmingOverride? Find(string? monitorId)
    {
        var normalizedId = NormalizeId(monitorId);
        return normalizedId is null
            ? null
            : Clamp().Entries.FirstOrDefault(entry => string.Equals(entry.MonitorId, normalizedId, StringComparison.OrdinalIgnoreCase));
    }

    public MonitorManualDimmingOverride? FindForMonitor(MonitorDescriptor monitor) =>
        Clamp().Entries.FirstOrDefault(entry => entry.Matches(monitor));

    public MonitorManualDimmingOptions Upsert(MonitorManualDimmingOverride entry)
    {
        var incoming = entry.Clamp();
        if (string.IsNullOrWhiteSpace(incoming.MonitorId))
        {
            return Clamp();
        }

        var entries = Clamp().Entries.ToList();
        var existingIndex = entries.FindIndex(current =>
            string.Equals(current.MonitorId, incoming.MonitorId, StringComparison.OrdinalIgnoreCase));

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

    public MonitorManualDimmingOptions Remove(string? monitorId)
    {
        var normalizedId = NormalizeId(monitorId);
        return normalizedId is null
            ? Clamp()
            : this with
            {
                Entries = Clamp().Entries
                    .Where(entry => !string.Equals(entry.MonitorId, normalizedId, StringComparison.OrdinalIgnoreCase))
                    .ToArray()
            };
    }

    private static string? NormalizeId(string? monitorId) =>
        string.IsNullOrWhiteSpace(monitorId) ? null : monitorId.Trim();
}
