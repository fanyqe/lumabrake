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

    [Fact]
    public void Upsert_ReplacesMatchingScheduleWithoutMovingPriority()
    {
        var schedules = new ScheduleOptions
        {
            Entries =
            [
                new ScheduledEffect { Id = "work", Name = "Work", ManualDimmingOpacity = 0.10 },
                new ScheduledEffect { Id = "night", Name = "Night", ManualDimmingOpacity = 0.20 }
            ]
        }.Upsert(new ScheduledEffect { Id = "WORK", Name = "Updated", ManualDimmingOpacity = 0.30 });

        Assert.Collection(
            schedules.Entries,
            entry =>
            {
                Assert.Equal("WORK", entry.Id);
                Assert.Equal("Updated", entry.Name);
                Assert.Equal(0.30, entry.ManualDimmingOpacity, precision: 12);
            },
            entry => Assert.Equal("night", entry.Id));
        Assert.Equal("Updated", schedules.Find("work")?.Name);
    }

    [Fact]
    public void Remove_DropsMatchingScheduleOnly()
    {
        var schedules = new ScheduleOptions
        {
            Entries =
            [
                new ScheduledEffect { Id = "work" },
                new ScheduledEffect { Id = "night" }
            ]
        }.Remove("WORK");

        var schedule = Assert.Single(schedules.Entries);
        Assert.Equal("night", schedule.Id);
    }

    [Fact]
    public void SetEntryEnabled_TogglesOnlyMatchingSchedule()
    {
        var schedules = new ScheduleOptions
        {
            Entries =
            [
                new ScheduledEffect { Id = "work", Enabled = true },
                new ScheduledEffect { Id = "night", Enabled = true }
            ]
        }.SetEntryEnabled("WORK", enabled: false);

        Assert.False(schedules.Find("work")?.Enabled);
        Assert.True(schedules.Find("night")?.Enabled);
    }

    [Fact]
    public void FindAndRemove_IgnoreBlankIds()
    {
        var schedules = new ScheduleOptions
        {
            Entries = [new ScheduledEffect { Id = "night" }]
        };

        Assert.Null(schedules.Find(" "));
        Assert.Single(schedules.Remove(" ").Entries);
    }
}
