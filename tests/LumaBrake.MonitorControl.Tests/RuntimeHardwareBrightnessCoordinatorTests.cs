using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Hardware;
using LumaBrake.Domain.Schedules;
using LumaBrake.Persistence;

namespace LumaBrake.MonitorControl.Tests;

public sealed class RuntimeHardwareBrightnessCoordinatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lumabrake-runtime-hardware-{Guid.NewGuid():N}");

    [Fact]
    public void ApplyIfChanged_DoesNothingWhenHardwareWritesAreDisabled()
    {
        var writer = new RecordingWriter([]);
        var coordinator = new RuntimeHardwareBrightnessCoordinator(
            writer,
            new HardwareBrightnessStateStore(rootDirectory: _root));

        var result = coordinator.ApplyIfChanged(SettingsWithStartupTarget(writesAllowed: false), TestNow());

        Assert.False(result.TargetConfigured);
        Assert.False(result.TargetChanged);
        Assert.Empty(result.WriteResults);
        Assert.Empty(writer.Calls);
    }

    [Fact]
    public void ApplyIfChanged_SkipsSeededStartupTarget()
    {
        var writer = new RecordingWriter([]);
        var settings = SettingsWithStartupTarget(writesAllowed: true);
        var coordinator = new RuntimeHardwareBrightnessCoordinator(
            writer,
            new HardwareBrightnessStateStore(rootDirectory: _root));

        coordinator.Seed(settings, TestNow());
        var result = coordinator.ApplyIfChanged(settings, TestNow());

        Assert.True(result.TargetConfigured);
        Assert.False(result.TargetChanged);
        Assert.Empty(result.WriteResults);
        Assert.Empty(writer.Calls);
    }

    [Fact]
    public void ApplyIfChanged_WritesProfileOrStartupTargetWhenNoScheduleTargetIsActive()
    {
        var now = TestNow();
        var writer = new RecordingWriter([Success(@"\\.\DISPLAY3", physicalMonitorIndex: 2, brightness: 45)]);
        var stateStore = new HardwareBrightnessStateStore(rootDirectory: _root);
        var coordinator = new RuntimeHardwareBrightnessCoordinator(writer, stateStore);

        var result = coordinator.ApplyIfChanged(SettingsWithStartupTarget(writesAllowed: true), now);

        Assert.True(result.TargetConfigured);
        Assert.True(result.TargetChanged);
        Assert.True(result.NativeWriteAttemptRecorded);
        Assert.False(result.SoftwareFallbackApplied);
        var call = Assert.Single(writer.Calls);
        Assert.Equal(@"\\.\DISPLAY3", call.DisplayDeviceName);
        Assert.Equal(2, call.PhysicalMonitorIndex);
        Assert.Equal(45, call.RequestedBrightness);
        Assert.Equal(now, stateStore.Load().LastWriteAttemptAt(@"\\.\DISPLAY3#2"));
    }

    [Fact]
    public void ApplyIfChanged_UsesActiveScheduleTargetBeforeStartupTarget()
    {
        var now = TestNow();
        var writer = new RecordingWriter([Success(@"\\.\DISPLAY2", physicalMonitorIndex: 1, brightness: 70)]);
        var coordinator = new RuntimeHardwareBrightnessCoordinator(
            writer,
            new HardwareBrightnessStateStore(rootDirectory: _root));

        var result = coordinator.ApplyIfChanged(SettingsWithActiveScheduleTarget(), now);

        Assert.True(result.TargetConfigured);
        Assert.True(result.TargetChanged);
        var call = Assert.Single(writer.Calls);
        Assert.Equal(@"\\.\DISPLAY2", call.DisplayDeviceName);
        Assert.Equal(1, call.PhysicalMonitorIndex);
        Assert.Equal(70, call.RequestedBrightness);
    }

    [Fact]
    public void ApplyIfChanged_DoesNotRepeatSuccessfulTarget()
    {
        var writer = new RecordingWriter([Success(@"\\.\DISPLAY3", physicalMonitorIndex: 2, brightness: 45)]);
        var coordinator = new RuntimeHardwareBrightnessCoordinator(
            writer,
            new HardwareBrightnessStateStore(rootDirectory: _root));
        var settings = SettingsWithStartupTarget(writesAllowed: true);

        var first = coordinator.ApplyIfChanged(settings, TestNow());
        var second = coordinator.ApplyIfChanged(settings, TestNow().AddSeconds(30));

        Assert.True(first.TargetChanged);
        Assert.False(second.TargetChanged);
        Assert.Single(writer.Calls);
    }

    [Fact]
    public void ApplyIfChanged_DoesNotRepeatFailedTargetWithoutFallback()
    {
        var writer = new RecordingWriter(
        [
            new HardwareBrightnessWriteResult(
                @"\\.\DISPLAY3",
                "Display 3",
                PhysicalMonitorIndex: 2,
                RequestedBrightness: 45,
                AppliedHardwareBrightness: 0,
                Succeeded: false,
                Error: "Display device or physical monitor index was not found.")
        ]);
        var coordinator = new RuntimeHardwareBrightnessCoordinator(
            writer,
            new HardwareBrightnessStateStore(rootDirectory: _root));
        var settings = SettingsWithStartupTarget(writesAllowed: true);

        var first = coordinator.ApplyIfChanged(settings, TestNow());
        var second = coordinator.ApplyIfChanged(settings, TestNow().AddSeconds(30));

        Assert.True(first.TargetChanged);
        Assert.False(second.TargetChanged);
        Assert.Single(writer.Calls);
    }

    [Fact]
    public void ApplyIfChanged_StoresSoftwareFallbackWhenScheduleWriteFails()
    {
        var writer = new RecordingWriter(
        [
            new HardwareBrightnessWriteResult(
                @"\\.\DISPLAY2",
                "Display 2",
                PhysicalMonitorIndex: 1,
                RequestedBrightness: 70,
                AppliedHardwareBrightness: 0,
                Succeeded: false,
                Error: "Display device or physical monitor index was not found.")
        ]);
        var coordinator = new RuntimeHardwareBrightnessCoordinator(
            writer,
            new HardwareBrightnessStateStore(rootDirectory: _root));

        var result = coordinator.ApplyIfChanged(SettingsWithActiveScheduleTarget(fallback: true), TestNow());

        Assert.True(result.TargetChanged);
        Assert.True(result.SoftwareFallbackApplied);
        var fallback = Assert.Single(result.Settings.MonitorManualDimming.Entries);
        Assert.Equal(@"\\.\DISPLAY2", fallback.MonitorId);
        Assert.Equal(0.30, fallback.Opacity, precision: 12);
    }

    [Fact]
    public void ApplyIfChanged_ReturnsToStartupTargetWhenScheduleStops()
    {
        var writer = new RecordingWriter([Success(@"\\.\DISPLAY3", physicalMonitorIndex: 2, brightness: 45)]);
        var coordinator = new RuntimeHardwareBrightnessCoordinator(
            writer,
            new HardwareBrightnessStateStore(rootDirectory: _root));
        var settings = SettingsWithLimitedScheduleTarget(includeStartupTarget: true);

        coordinator.Seed(settings, TestNow());
        var result = coordinator.ApplyIfChanged(settings, TestNow().AddHours(2));

        Assert.True(result.TargetConfigured);
        Assert.True(result.TargetChanged);
        var call = Assert.Single(writer.Calls);
        Assert.Equal(@"\\.\DISPLAY3", call.DisplayDeviceName);
        Assert.Equal(2, call.PhysicalMonitorIndex);
        Assert.Equal(45, call.RequestedBrightness);
    }

    [Fact]
    public void ApplyIfChanged_DoesNotWriteWhenScheduleStopsWithoutStartupTarget()
    {
        var writer = new RecordingWriter([]);
        var coordinator = new RuntimeHardwareBrightnessCoordinator(
            writer,
            new HardwareBrightnessStateStore(rootDirectory: _root));
        var settings = SettingsWithLimitedScheduleTarget(includeStartupTarget: false);

        coordinator.Seed(settings, TestNow());
        var result = coordinator.ApplyIfChanged(settings, TestNow().AddHours(2));

        Assert.False(result.TargetConfigured);
        Assert.False(result.TargetChanged);
        Assert.Empty(writer.Calls);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static DateTimeOffset TestNow() =>
        new(2026, 7, 27, 12, 0, 0, TimeSpan.FromHours(3));

    private static LumaBrakeSettings SettingsWithStartupTarget(bool writesAllowed) =>
        new()
        {
            HardwareBrightness = new HardwareBrightnessOptions { Enabled = true, AllowWrites = writesAllowed },
            StartupRestoration = new StartupRestorationOptions
            {
                HardwareBrightness = new HardwareBrightnessTarget
                {
                    Enabled = true,
                    DisplayDeviceName = @"\\.\DISPLAY3",
                    PhysicalMonitorIndex = 2,
                    Brightness = 45
                }
            }
        };

    private static LumaBrakeSettings SettingsWithActiveScheduleTarget(bool fallback = false) =>
        SettingsWithStartupTarget(writesAllowed: true) with
        {
            Schedules = new ScheduleOptions
            {
                Enabled = true,
                Entries =
                [
                    new ScheduledEffect
                    {
                        Id = "work",
                        Name = "Work",
                        DayMask = ScheduleDayMask.EveryDay,
                        StartMinuteOfDay = 0,
                        EndMinuteOfDay = 1439,
                        HardwareBrightness = new HardwareBrightnessTarget
                        {
                            Enabled = true,
                            DisplayDeviceName = @"\\.\DISPLAY2",
                            PhysicalMonitorIndex = 1,
                            Brightness = 70,
                            UseSoftwareFallback = fallback
                        }
                    }
                ]
            },
            MonitorManualDimming = new MonitorManualDimmingOptions()
        };

    private static LumaBrakeSettings SettingsWithLimitedScheduleTarget(bool includeStartupTarget) =>
        SettingsWithStartupTarget(writesAllowed: true) with
        {
            StartupRestoration = new StartupRestorationOptions
            {
                HardwareBrightness = includeStartupTarget
                    ? SettingsWithStartupTarget(writesAllowed: true).StartupRestoration.HardwareBrightness
                    : new HardwareBrightnessTarget()
            },
            Schedules = new ScheduleOptions
            {
                Enabled = true,
                Entries =
                [
                    new ScheduledEffect
                    {
                        Id = "work",
                        Name = "Work",
                        DayMask = ScheduleDayMask.EveryDay,
                        StartMinuteOfDay = 11 * 60,
                        EndMinuteOfDay = 13 * 60,
                        HardwareBrightness = new HardwareBrightnessTarget
                        {
                            Enabled = true,
                            DisplayDeviceName = @"\\.\DISPLAY2",
                            PhysicalMonitorIndex = 1,
                            Brightness = 70
                        }
                    }
                ]
            },
            MonitorManualDimming = new MonitorManualDimmingOptions()
        };

    private static HardwareBrightnessWriteResult Success(string displayDeviceName, int physicalMonitorIndex, int brightness) =>
        new(
            displayDeviceName,
            $"Display {physicalMonitorIndex}",
            physicalMonitorIndex,
            brightness,
            brightness,
            Succeeded: true,
            Error: null);

    private sealed class RecordingWriter : IHardwareBrightnessWriter
    {
        private readonly Queue<IReadOnlyList<HardwareBrightnessWriteResult>> _results;

        public RecordingWriter(IReadOnlyList<HardwareBrightnessWriteResult> results)
        {
            _results = new Queue<IReadOnlyList<HardwareBrightnessWriteResult>>([results]);
        }

        public List<WriteCall> Calls { get; } = [];

        public IReadOnlyList<HardwareBrightnessWriteResult> SetBrightness(
            string displayDeviceName,
            int physicalMonitorIndex,
            int requestedBrightness,
            HardwareBrightnessOptions options,
            DateTimeOffset now,
            DateTimeOffset? lastWriteAt)
        {
            Calls.Add(new(displayDeviceName, physicalMonitorIndex, requestedBrightness, options, now, lastWriteAt));
            return _results.Count == 0 ? [] : _results.Dequeue();
        }
    }

    private sealed record WriteCall(
        string DisplayDeviceName,
        int PhysicalMonitorIndex,
        int RequestedBrightness,
        HardwareBrightnessOptions Options,
        DateTimeOffset Timestamp,
        DateTimeOffset? LastWriteAt);
}
