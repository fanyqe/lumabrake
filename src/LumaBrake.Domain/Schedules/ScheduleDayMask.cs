namespace LumaBrake.Domain.Schedules;

public static class ScheduleDayMask
{
    public const int Sunday = 1 << 0;
    public const int Monday = 1 << 1;
    public const int Tuesday = 1 << 2;
    public const int Wednesday = 1 << 3;
    public const int Thursday = 1 << 4;
    public const int Friday = 1 << 5;
    public const int Saturday = 1 << 6;
    public const int EveryDay = Sunday | Monday | Tuesday | Wednesday | Thursday | Friday | Saturday;
    public const int Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday;
    public const int Weekend = Saturday | Sunday;

    public static int For(DayOfWeek day) => 1 << (int)day;

    public static bool Includes(int dayMask, DayOfWeek day) =>
        (Normalize(dayMask) & For(day)) != 0;

    public static int Normalize(int dayMask) => dayMask & EveryDay;

    public static DayOfWeek Previous(DayOfWeek day) =>
        day == DayOfWeek.Sunday ? DayOfWeek.Saturday : (DayOfWeek)((int)day - 1);
}
