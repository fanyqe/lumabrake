using LumaBrake.Domain.Hardware;
using LumaBrake.MonitorControl;

namespace LumaBrake.MonitorControl.Tests;

public sealed class DdcCiBrightnessWriterTests
{
    [Fact]
    public void SetBrightness_RejectsMissingDisplayDeviceBeforeNativeAccess()
    {
        var result = Assert.Single(new DdcCiBrightnessWriter().SetBrightness(
            " ",
            physicalMonitorIndex: 0,
            requestedBrightness: 50,
            new HardwareBrightnessOptions { Enabled = true, AllowWrites = true },
            DateTimeOffset.UnixEpoch,
            lastWriteAt: null));

        Assert.False(result.Succeeded);
        Assert.Equal("Display device name is required.", result.Error);
    }

    [Fact]
    public void SetBrightness_RejectsInvalidTargetBeforeNativeAccess()
    {
        var writer = new DdcCiBrightnessWriter();

        var invalidIndex = Assert.Single(writer.SetBrightness(
            @"\\.\DISPLAY1",
            physicalMonitorIndex: -1,
            requestedBrightness: 50,
            new HardwareBrightnessOptions { Enabled = true, AllowWrites = true },
            DateTimeOffset.UnixEpoch,
            lastWriteAt: null));
        var invalidBrightness = Assert.Single(writer.SetBrightness(
            @"\\.\DISPLAY1",
            physicalMonitorIndex: 0,
            requestedBrightness: 101,
            new HardwareBrightnessOptions { Enabled = true, AllowWrites = true },
            DateTimeOffset.UnixEpoch,
            lastWriteAt: null));

        Assert.False(invalidIndex.Succeeded);
        Assert.Equal("Physical monitor index must be 0 or greater.", invalidIndex.Error);
        Assert.False(invalidBrightness.Succeeded);
        Assert.Equal("Hardware brightness must be from 0 to 100.", invalidBrightness.Error);
    }

    [Fact]
    public void SetBrightness_RejectsDisabledWritePolicyBeforeNativeAccess()
    {
        var result = Assert.Single(new DdcCiBrightnessWriter().SetBrightness(
            @"\\.\DISPLAY1",
            physicalMonitorIndex: 0,
            requestedBrightness: 50,
            new HardwareBrightnessOptions { Enabled = true, AllowWrites = false },
            DateTimeOffset.UnixEpoch,
            lastWriteAt: null));

        Assert.False(result.Succeeded);
        Assert.Equal("Hardware brightness writes are disabled.", result.Error);
    }

    [Fact]
    public void SetBrightness_RejectsRateLimitedWritePolicyBeforeNativeAccess()
    {
        var now = DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(12);

        var result = Assert.Single(new DdcCiBrightnessWriter().SetBrightness(
            @"\\.\DISPLAY1",
            physicalMonitorIndex: 0,
            requestedBrightness: 50,
            new HardwareBrightnessOptions
            {
                Enabled = true,
                AllowWrites = true,
                MinimumWriteInterval = TimeSpan.FromSeconds(10)
            },
            now,
            lastWriteAt: now - TimeSpan.FromSeconds(9)));

        Assert.False(result.Succeeded);
        Assert.Equal("Hardware brightness was written too recently.", result.Error);
    }

    [Theory]
    [InlineData(0, 10u)]
    [InlineData(50, 55u)]
    [InlineData(100, 100u)]
    public void ScaleBrightnessPercentage_MapsPercentToMonitorRange(int requestedBrightness, uint expected)
    {
        var actual = DdcCiBrightnessWriter.ScaleBrightnessPercentage(requestedBrightness, minimum: 10, maximum: 100);

        Assert.Equal(expected, actual);
    }
}
