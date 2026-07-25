using LumaBrake.Domain.Schedules;

namespace LumaBrake.Domain.Tests;

public sealed class ScheduledEffectTests
{
    [Fact]
    public void IsActiveAt_MatchesSameDayWindow()
    {
        var schedule = new ScheduledEffect
        {
            DayMask = ScheduleDayMask.Weekdays,
            StartMinuteOfDay = 9 * 60,
            EndMinuteOfDay = 17 * 60
        };

        Assert.True(schedule.IsActiveAt(DayOfWeek.Monday, new TimeSpan(10, 0, 0)));
        Assert.False(schedule.IsActiveAt(DayOfWeek.Monday, new TimeSpan(17, 0, 0)));
        Assert.False(schedule.IsActiveAt(DayOfWeek.Saturday, new TimeSpan(10, 0, 0)));
    }

    [Fact]
    public void IsActiveAt_UsesPreviousDayForOvernightWindow()
    {
        var schedule = new ScheduledEffect
        {
            DayMask = ScheduleDayMask.Friday,
            StartMinuteOfDay = 22 * 60,
            EndMinuteOfDay = 7 * 60
        };

        Assert.True(schedule.IsActiveAt(DayOfWeek.Friday, new TimeSpan(23, 0, 0)));
        Assert.True(schedule.IsActiveAt(DayOfWeek.Saturday, new TimeSpan(1, 0, 0)));
        Assert.False(schedule.IsActiveAt(DayOfWeek.Sunday, new TimeSpan(1, 0, 0)));
    }

    [Fact]
    public void IsActiveAt_IgnoresDisabledOrZeroLengthSchedule()
    {
        var disabled = new ScheduledEffect { Enabled = false };
        var zeroLength = new ScheduledEffect { StartMinuteOfDay = 8 * 60, EndMinuteOfDay = 8 * 60 };

        Assert.False(disabled.IsActiveAt(DayOfWeek.Monday, new TimeSpan(23, 0, 0)));
        Assert.False(zeroLength.IsActiveAt(DayOfWeek.Monday, new TimeSpan(8, 0, 0)));
    }

    [Fact]
    public void ActiveAt_ReturnsFirstMatchingClampedEntry()
    {
        var schedules = new ScheduleOptions
        {
            Enabled = true,
            Entries =
            [
                new ScheduledEffect { Id = "disabled", Enabled = false },
                new ScheduledEffect
                {
                    Id = "night",
                    DayMask = ScheduleDayMask.EveryDay,
                    StartMinuteOfDay = 22 * 60,
                    EndMinuteOfDay = 7 * 60,
                    ManualDimmingOpacity = 2
                }
            ]
        };

        var active = schedules.ActiveAt(new DateTimeOffset(2026, 7, 26, 23, 0, 0, TimeSpan.FromHours(3)));

        Assert.NotNull(active);
        Assert.Equal("night", active.Id);
        Assert.Equal(0.90, active.ManualDimmingOpacity, precision: 12);
    }
}
