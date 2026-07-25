using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Monitors;

namespace LumaBrake.Domain.Tests;

public sealed class MonitorTintOptionsTests
{
    [Fact]
    public void Clamp_NormalizesMonitorIdsAndTintValues()
    {
        var options = new MonitorTintOptions
        {
            Entries =
            [
                new MonitorTintOverride
                {
                    MonitorId = "  monitor-1  ",
                    Tint = new TintOptions { Enabled = true, Red = 255, Green = 214, Blue = 170, Opacity = 3 }
                },
                new MonitorTintOverride { MonitorId = " " }
            ]
        }.Clamp();

        var entry = Assert.Single(options.Entries);
        Assert.Equal("monitor-1", entry.MonitorId);
        Assert.True(entry.Tint.Enabled);
        Assert.Equal(0.60, entry.Tint.Opacity, precision: 12);
    }

    [Fact]
    public void Upsert_ReplacesMatchingMonitorCaseInsensitively()
    {
        var options = new MonitorTintOptions()
            .Upsert(new MonitorTintOverride { MonitorId = "DISPLAY1", Tint = new TintOptions { Enabled = true, Opacity = 0.10 } })
            .Upsert(new MonitorTintOverride { MonitorId = "display1", Tint = new TintOptions { Enabled = true, Opacity = 0.20 } });

        var entry = Assert.Single(options.Entries);
        Assert.Equal("display1", entry.MonitorId);
        Assert.Equal(0.20, entry.Tint.Opacity, precision: 12);
        var found = options.Find("DISPLAY1");
        Assert.NotNull(found);
        Assert.Equal(0.20, found!.Tint.Opacity, precision: 12);
    }

    [Fact]
    public void FindForMonitor_MatchesStableIdOrDeviceName()
    {
        var monitor = Monitor("stable-1", @"\\.\DISPLAY2");
        var options = new MonitorTintOptions
        {
            Entries =
            [
                new MonitorTintOverride { MonitorId = "stable-1", Tint = new TintOptions { Enabled = true, Opacity = 0.12 } },
                new MonitorTintOverride { MonitorId = @"\\.\DISPLAY3", Tint = new TintOptions { Enabled = true, Opacity = 0.22 } }
            ]
        };

        var stableMatch = options.FindForMonitor(monitor);
        Assert.NotNull(stableMatch);
        Assert.Equal(0.12, stableMatch!.Tint.Opacity, precision: 12);
        Assert.NotNull(new MonitorTintOptions
        {
            Entries = [new MonitorTintOverride { MonitorId = @"\\.\DISPLAY2" }]
        }.FindForMonitor(monitor));
    }

    [Fact]
    public void Remove_DropsMatchingMonitorOnly()
    {
        var options = new MonitorTintOptions
        {
            Entries =
            [
                new MonitorTintOverride { MonitorId = "one" },
                new MonitorTintOverride { MonitorId = "two" }
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
