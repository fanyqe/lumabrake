using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Hardware;
using LumaBrake.Domain.Schedules;
using LumaBrake.Persistence;

namespace LumaBrake.MonitorControl.Tests;

public sealed class StartupHardwareBrightnessRestorerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lumabrake-hardware-restore-{Guid.NewGuid():N}");

    [Fact]
    public void Apply_DoesNothingWhenNoTargetIsConfigured()
    {
        var writer = new RecordingWriter([]);
        var restorer = new StartupHardwareBrightnessRestorer(
            writer,
            new HardwareBrightnessStateStore(rootDirectory: _root));

        var result = restorer.Apply(new LumaBrakeSettings(), DateTimeOffset.UtcNow);

        Assert.False(result.TargetConfigured);
        Assert.Empty(result.WriteResults);
        Assert.Equal(0, writer.CallCount);
    }

    [Fact]
    public void Apply_WritesConfiguredTargetAndRecordsNativeAttempt()
    {
        var now = new DateTimeOffset(2026, 7, 26, 21, 45, 0, TimeSpan.Zero);
        var writer = new RecordingWriter(
        [
            new HardwareBrightnessWriteResult(
                @"\\.\DISPLAY2",
                "Display 2",
                PhysicalMonitorIndex: 1,
                RequestedBrightness: 70,
                AppliedHardwareBrightness: 70,
                Succeeded: true,
                Error: null)
        ]);
        var stateStore = new HardwareBrightnessStateStore(rootDirectory: _root);
        var restorer = new StartupHardwareBrightnessRestorer(writer, stateStore);

        var result = restorer.Apply(SettingsWithTarget(fallback: false), now);

        Assert.True(result.TargetConfigured);
        Assert.True(result.NativeWriteAttemptRecorded);
        Assert.False(result.SoftwareFallbackApplied);
        Assert.Equal(1, writer.CallCount);
        Assert.Equal(now, stateStore.Load().LastWriteAttemptAt(@"\\.\DISPLAY2#1"));
    }

    [Fact]
    public void Apply_StoresSoftwareFallbackWhenConfiguredWriteFails()
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
        var restorer = new StartupHardwareBrightnessRestorer(
            writer,
            new HardwareBrightnessStateStore(rootDirectory: _root));

        var result = restorer.Apply(SettingsWithTarget(fallback: true), DateTimeOffset.UtcNow);

        Assert.True(result.TargetConfigured);
        Assert.True(result.SoftwareFallbackApplied);
        var fallback = Assert.Single(result.Settings.MonitorManualDimming.Entries);
        Assert.Equal(@"\\.\DISPLAY2", fallback.MonitorId);
        Assert.Equal(0.30, fallback.Opacity, precision: 12);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static LumaBrakeSettings SettingsWithTarget(bool fallback) =>
        new()
        {
            HardwareBrightness = new HardwareBrightnessOptions { Enabled = true, AllowWrites = true },
            StartupRestoration = new StartupRestorationOptions
            {
                HardwareBrightness = new HardwareBrightnessTarget
                {
                    Enabled = true,
                    DisplayDeviceName = @"\\.\DISPLAY2",
                    PhysicalMonitorIndex = 1,
                    Brightness = 70,
                    UseSoftwareFallback = fallback
                }
            },
            MonitorManualDimming = new MonitorManualDimmingOptions()
        };

    private sealed class RecordingWriter : IHardwareBrightnessWriter
    {
        private readonly IReadOnlyList<HardwareBrightnessWriteResult> _results;

        public RecordingWriter(IReadOnlyList<HardwareBrightnessWriteResult> results)
        {
            _results = results;
        }

        public int CallCount { get; private set; }

        public IReadOnlyList<HardwareBrightnessWriteResult> SetBrightness(
            string displayDeviceName,
            int physicalMonitorIndex,
            int requestedBrightness,
            HardwareBrightnessOptions options,
            DateTimeOffset now,
            DateTimeOffset? lastWriteAt)
        {
            CallCount++;
            Assert.Equal(@"\\.\DISPLAY2", displayDeviceName);
            Assert.Equal(1, physicalMonitorIndex);
            Assert.Equal(70, requestedBrightness);
            Assert.True(options.Enabled);
            Assert.True(options.AllowWrites);
            Assert.Null(lastWriteAt);
            return _results;
        }
    }
}
