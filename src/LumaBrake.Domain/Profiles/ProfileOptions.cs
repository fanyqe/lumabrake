namespace LumaBrake.Domain.Profiles;

public sealed record ProfileOptions
{
    public LumaBrakeProfile[] Entries { get; init; } = [];

    public ProfileOptions Clamp()
    {
        var entries = Entries
            .Select(entry => entry.Clamp())
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return this with { Entries = entries };
    }

    public LumaBrakeProfile? Find(string? id)
    {
        var normalizedId = NormalizeId(id);
        return normalizedId is null
            ? null
            : Clamp().Entries.FirstOrDefault(entry => string.Equals(entry.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
    }

    public ProfileOptions Upsert(LumaBrakeProfile profile)
    {
        var incoming = profile.Clamp();
        var current = Clamp().Entries
            .Where(entry => !string.Equals(entry.Id, incoming.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var entries = new LumaBrakeProfile[current.Length + 1];
        Array.Copy(current, entries, current.Length);
        entries[^1] = incoming;

        return this with { Entries = entries };
    }

    public ProfileOptions Remove(string? id)
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

    private static string? NormalizeId(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : id.Trim();
}
