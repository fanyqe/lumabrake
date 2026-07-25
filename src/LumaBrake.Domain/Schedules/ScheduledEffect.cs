using LumaBrake.Domain.Effects;

namespace LumaBrake.Domain.Schedules;

public sealed record ScheduledEffect
{
    public string Id { get; init; } = "schedule";

    public string Name { get; init; } = "Schedule";

    public bool Enabled { get; init; } = true;

    public int DayMask { get; init; } = ScheduleDayMask.EveryDay;

    public int StartMinuteOfDay { get; init; } = 22 * 60;

    public int EndMinuteOfDay { get; init; } = 7 * 60;

    public double ManualDimmingOpacity { get; init; } = 0.20;

    public TintOptions Tint { get; init; } = new();

    public bool IsActiveAt(DateTimeOffset localTimestamp) =>
        IsActiveAt(localTimestamp.DayOfWeek, localTimestamp.TimeOfDay);

    public bool IsActiveAt(DayOfWeek day, TimeSpan localTime)
    {
        var effect = Clamp();
        if (!effect.Enabled || effect.StartMinuteOfDay == effect.EndMinuteOfDay)
        {
            return false;
        }

        var currentMinute = MinuteOfDay(localTime);
        if (effect.StartMinuteOfDay < effect.EndMinuteOfDay)
        {
            return ScheduleDayMask.Includes(effect.DayMask, day) &&
                currentMinute >= effect.StartMinuteOfDay &&
                currentMinute < effect.EndMinuteOfDay;
        }

        if (currentMinute >= effect.StartMinuteOfDay)
        {
            return ScheduleDayMask.Includes(effect.DayMask, day);
        }

        return currentMinute < effect.EndMinuteOfDay &&
            ScheduleDayMask.Includes(effect.DayMask, ScheduleDayMask.Previous(day));
    }

    public ScheduledEffect Clamp() =>
        this with
        {
            Id = string.IsNullOrWhiteSpace(Id) ? "schedule" : Id.Trim(),
            Name = string.IsNullOrWhiteSpace(Name) ? "Schedule" : Name.Trim(),
            DayMask = ScheduleDayMask.Normalize(DayMask),
            StartMinuteOfDay = Math.Clamp(StartMinuteOfDay, 0, 1439),
            EndMinuteOfDay = Math.Clamp(EndMinuteOfDay, 0, 1439),
            ManualDimmingOpacity = Math.Clamp(ManualDimmingOpacity, 0, 0.90)
        };

    private static int MinuteOfDay(TimeSpan localTime)
    {
        var totalMinutes = (int)Math.Floor(localTime.TotalMinutes);
        var normalized = totalMinutes % 1440;
        return normalized < 0 ? normalized + 1440 : normalized;
    }
}
