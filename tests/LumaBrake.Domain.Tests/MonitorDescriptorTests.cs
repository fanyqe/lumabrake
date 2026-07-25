using LumaBrake.Domain.Monitors;

namespace LumaBrake.Domain.Tests;

public sealed class MonitorDescriptorTests
{
    [Fact]
    public void MonitorBounds_ComputesRightAndBottom()
    {
        var bounds = new MonitorBounds(-1920, 120, 1920, 1080);

        Assert.Equal(0, bounds.Right);
        Assert.Equal(1200, bounds.Bottom);
    }

    [Fact]
    public void MonitorDescriptor_PreservesPrimaryAndWorkAreaState()
    {
        var descriptor = new MonitorDescriptor(
            "display-1",
            @"\\.\DISPLAY1",
            "Internal Display",
            IsPrimary: true,
            IsActive: true,
            new MonitorBounds(0, 0, 2560, 1440),
            new MonitorBounds(0, 0, 2560, 1400),
            DpiScale: 1.25);

        Assert.True(descriptor.IsPrimary);
        Assert.True(descriptor.IsActive);
        Assert.Equal("display-1", descriptor.StableId);
        Assert.Equal(@"\\.\DISPLAY1", descriptor.DeviceName);
        Assert.Equal(1400, descriptor.WorkArea.Height);
        Assert.Equal(1.25, descriptor.DpiScale, precision: 12);
    }
}
