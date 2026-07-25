using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Monitors;

namespace LumaBrake.Domain.Tests;

public sealed class MonitorManualDimmingOptionsTests
{
    [Fact]
    public void Clamp_NormalizesMonitorIdsAndOpacityValues()
    {
        var options = new MonitorManualDimmingOptions
        {
            Entries =
            [
                new MonitorManualDimmingOverride { MonitorId = "  monitor-1  ", Opacity = 3 },
                new MonitorManualDimmingOverride { MonitorId = " " }
            ]
        }.Clamp();

        var entry = Assert.Single(options.Entries);
        Assert.Equal("monitor-1", entry.MonitorId);
        Assert.Equal(0.90, entry.Opacity, precision: 12);
    }

    [Fact]
    public void Upsert_ReplacesMatchingMonitorCaseInsensitively()
    {
        var options = new MonitorManualDimmingOptions()
            .Upsert(new MonitorManualDimmingOverride { MonitorId = "DISPLAY1", Opacity = 0.10 })
            .Upsert(new MonitorManualDimmingOverride { MonitorId = "display1", Opacity = 0.20 });

        var entry = Assert.Single(options.Entries);
        Assert.Equal("display1", entry.MonitorId);
        Assert.Equal(0.20, entry.Opacity, precision: 12);
        var found = options.Find("DISPLAY1");
        Assert.NotNull(found);
        Assert.Equal(0.20, found!.Opacity, precision: 12);
    }

    [Fact]
    public void FindForMonitor_MatchesStableIdOrDeviceName()
    {
        var monitor = Monitor("stable-1", @"\\.\DISPLAY2");
        var options = new MonitorManualDimmingOptions
        {
            Entries =
            [
                new MonitorManualDimmingOverride { MonitorId = "stable-1", Opacity = 0.12 },
                new MonitorManualDimmingOverride { MonitorId = @"\\.\DISPLAY3", Opacity = 0.22 }
            ]
        };

        var stableMatch = options.FindForMonitor(monitor);
        Assert.NotNull(stableMatch);
        Assert.Equal(0.12, stableMatch!.Opacity, precision: 12);
        Assert.NotNull(new MonitorManualDimmingOptions
        {
            Entries = [new MonitorManualDimmingOverride { MonitorId = @"\\.\DISPLAY2" }]
        }.FindForMonitor(monitor));
    }

    [Fact]
    public void Remove_DropsMatchingMonitorOnly()
    {
        var options = new MonitorManualDimmingOptions
        {
            Entries =
            [
                new MonitorManualDimmingOverride { MonitorId = "one" },
                new MonitorManualDimmingOverride { MonitorId = "two" }
            ]
        }.Remove("ONE");

        var entry = Assert.Single(options.Entries);
        Assert.Equal("two", entry.MonitorId);
    }

    private static MonitorDescriptor Monitor(string stableId, string deviceName) =>
        new(
            stableId,
            deviceName,
            "Display",
            IsPrimary: false,
            IsActive: true,
            new MonitorBounds(0, 0, 1920, 1080),
            new MonitorBounds(0, 0, 1920, 1040));
}
