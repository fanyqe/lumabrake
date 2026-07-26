using LumaBrake.Domain.Hardware;

namespace LumaBrake.Domain.Tests;

public sealed class HardwareBrightnessPolicyTests
{
    [Fact]
    public void CanRead_RequiresHardwareBrightnessToBeEnabled()
    {
        var policy = new HardwareBrightnessPolicy(new HardwareBrightnessOptions { Enabled = false });

        Assert.False(policy.CanRead(@"\\.\DISPLAY1", DateTimeOffset.UnixEpoch, lastReadAt: null));
    }

    [Fact]
    public void CanRead_RejectsBlacklistedDisplayDevice()
    {
        var policy = new HardwareBrightnessPolicy(new HardwareBrightnessOptions
        {
            Enabled = true,
            BlacklistedDisplayDeviceNames = [@"\\.\DISPLAY2"]
        });

        Assert.False(policy.CanRead(@"\\.\display2", DateTimeOffset.UnixEpoch, lastReadAt: null));
    }

    [Fact]
    public void CanRead_EnforcesMinimumReadInterval()
    {
        var policy = new HardwareBrightnessPolicy(new HardwareBrightnessOptions
        {
            Enabled = true,
            MinimumReadInterval = TimeSpan.FromSeconds(5)
        });

        var now = DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(10);

        Assert.False(policy.CanRead(@"\\.\DISPLAY1", now, now - TimeSpan.FromSeconds(4)));
        Assert.True(policy.CanRead(@"\\.\DISPLAY1", now, now - TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void CanWrite_RequiresExplicitWritePermissionAndInterval()
    {
        var now = DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(20);
        var disabledWrites = new HardwareBrightnessPolicy(new HardwareBrightnessOptions
        {
            Enabled = true,
            AllowWrites = false
        });
        var enabledWrites = new HardwareBrightnessPolicy(new HardwareBrightnessOptions
        {
            Enabled = true,
            AllowWrites = true,
            MinimumWriteInterval = TimeSpan.FromSeconds(10)
        });

        Assert.False(disabledWrites.CanWrite(@"\\.\DISPLAY1", now, lastWriteAt: null));
        Assert.False(enabledWrites.CanWrite(@"\\.\DISPLAY1", now, now - TimeSpan.FromSeconds(9)));
        Assert.True(enabledWrites.CanWrite(@"\\.\DISPLAY1", now, now - TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Clamp_NormalizesIntervalsAndBlacklist()
    {
        var options = new HardwareBrightnessOptions
        {
            MinimumReadInterval = TimeSpan.FromSeconds(-1),
            MinimumWriteInterval = TimeSpan.Zero,
            BlacklistedDisplayDeviceNames = [" ", @"\\.\DISPLAY1", @"\\.\display1"]
        }.Clamp();

        Assert.Equal(TimeSpan.Zero, options.MinimumReadInterval);
        Assert.Equal(TimeSpan.FromSeconds(1), options.MinimumWriteInterval);
        Assert.Equal([@"\\.\DISPLAY1"], options.BlacklistedDisplayDeviceNames);
    }
}
