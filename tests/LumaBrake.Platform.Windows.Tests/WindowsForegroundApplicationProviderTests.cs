using LumaBrake.Platform.Windows;

namespace LumaBrake.Platform.Windows.Tests;

public sealed class WindowsForegroundApplicationProviderTests
{
    [Fact]
    public void CoversMonitorBounds_ReturnsTrueWhenWindowMatchesMonitor()
    {
        var monitor = Rect(0, 0, 1920, 1080);

        var covers = WindowsForegroundApplicationProvider.CoversMonitorBounds(
            Rect(0, 0, 1920, 1080),
            monitor);

        Assert.True(covers);
    }

    [Fact]
    public void CoversMonitorBounds_AllowsSmallBorderTolerance()
    {
        var monitor = Rect(0, 0, 1920, 1080);

        var covers = WindowsForegroundApplicationProvider.CoversMonitorBounds(
            Rect(2, 2, 1918, 1078),
            monitor);

        Assert.True(covers);
    }

    [Fact]
    public void CoversMonitorBounds_ReturnsFalseWhenWindowDoesNotFillMonitor()
    {
        var monitor = Rect(0, 0, 1920, 1080);

        var covers = WindowsForegroundApplicationProvider.CoversMonitorBounds(
            Rect(0, 0, 1600, 900),
            monitor);

        Assert.False(covers);
    }

    [Fact]
    public void CoversMonitorBounds_ReturnsFalseForCollapsedWindow()
    {
        var monitor = Rect(0, 0, 1920, 1080);

        var covers = WindowsForegroundApplicationProvider.CoversMonitorBounds(
            Rect(0, 0, 0, 1080),
            monitor);

        Assert.False(covers);
    }

    private static NativeRect Rect(int left, int top, int right, int bottom) => new()
    {
        Left = left,
        Top = top,
        Right = right,
        Bottom = bottom
    };
}
