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

        return Entries
            .Select(entry => entry.Clamp())
            .FirstOrDefault(entry => entry.IsActiveAt(localTimestamp));
    }
}
