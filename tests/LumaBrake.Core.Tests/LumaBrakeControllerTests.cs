using System.Collections.Concurrent;
using LumaBrake.Capture;
using LumaBrake.Core;
using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Frames;
using LumaBrake.Domain.Monitors;
using LumaBrake.Persistence;
using LumaBrake.Rendering;

namespace LumaBrake.Core.Tests;

public sealed class LumaBrakeControllerTests
{
    [Fact]
    public void Constructor_BuildsPipelineSnapshotsForEveryDetectedMonitor()
    {
        var monitors = TestMonitors();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            new RecordingOverlay(),
            new LumaBrakeSettings(),
            new MutableMonitorCatalog(monitors));

        var snapshot = controller.Snapshot();

        Assert.Equal(2, snapshot.MonitorCount);
        Assert.Equal(2, snapshot.Monitors.Count);
        Assert.Contains(snapshot.Monitors, monitor => monitor.StableId == monitors[0].StableId && monitor.IsPrimary);
        Assert.Contains(snapshot.Monitors, monitor => monitor.StableId == monitors[1].StableId && !monitor.IsPrimary);
    }

    [Fact]
    public void RefreshMonitors_RemovesPipelinesForDisconnectedMonitors()
    {
        var monitors = TestMonitors();
        var catalog = new MutableMonitorCatalog(monitors);
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings(),
            catalog);

        catalog.Monitors = [monitors[1]];
        controller.RefreshMonitors();

        var snapshot = controller.Snapshot();

        Assert.Equal(1, snapshot.MonitorCount);
        Assert.Single(snapshot.Monitors);
        Assert.Equal(monitors[1].StableId, snapshot.Monitors[0].StableId);
        Assert.Equal([monitors[1].StableId], overlay.LastSyncedStableIds);
    }

    [Fact]
    public async Task Start_AnalyzesEachDetectedMonitorIndependently()
    {
        var monitors = TestMonitors();
        var capture = new RecordingCapture();
        using var controller = new LumaBrakeController(
            capture,
            new RecordingOverlay(),
            new LumaBrakeSettings(),
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var capturedBoth = await WaitUntilAsync(
            () => monitors.All(monitor => capture.CaptureCount(monitor.StableId) > 0),
            TimeSpan.FromSeconds(2));

        Assert.True(capturedBoth);
        Assert.All(monitors, monitor => Assert.True(capture.CaptureCount(monitor.StableId) > 0));

        var snapshotUpdated = await WaitUntilAsync(
            () => controller.Snapshot().FramesAnalyzed >= monitors.Length,
            TimeSpan.FromSeconds(2));

        Assert.True(snapshotUpdated);
    }

    [Fact]
    public void EmergencyDisable_HidesEveryOverlayAndClearsPipelineOpacity()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings(),
            new MutableMonitorCatalog(monitors));

        controller.EmergencyDisable();

        Assert.Equal(1, overlay.HideAllCalls);
        Assert.All(controller.Snapshot().Monitors, monitor => Assert.Equal(0, monitor.OverlayOpacity));
    }

    [Fact]
    public async Task Start_AddsRepetitiveFlashEventsForOnlyTheFlashingMonitor()
    {
        var monitors = TestMonitors();
        using var controller = new LumaBrakeController(
            new FlashingCapture(monitors[0].StableId),
            new RecordingOverlay(),
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                FlashGuard = new FlashDetectorOptions
                {
                    MinimumTransitions = 3,
                    MinimumAffectedTileRatio = 0.50,
                    Cooldown = TimeSpan.FromSeconds(5),
                    Hold = TimeSpan.FromSeconds(5),
                    Release = TimeSpan.FromSeconds(5)
                }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var flashDetected = await WaitUntilAsync(
            () =>
            {
                var snapshot = controller.Snapshot();
                var primary = snapshot.Monitors.Single(monitor => monitor.StableId == monitors[0].StableId);
                var secondary = snapshot.Monitors.Single(monitor => monitor.StableId == monitors[1].StableId);
                return primary.FlashEvents > 0 &&
                    primary.ActiveReason == EffectReason.RepetitiveFlash &&
                    secondary.FlashEvents == 0;
            },
            TimeSpan.FromSeconds(3));

        Assert.True(flashDetected);
    }

    private static MonitorDescriptor[] TestMonitors() =>
    [
        new(
            "display-1",
            @"\\.\DISPLAY1",
            "Primary",
            IsPrimary: true,
            IsActive: true,
            new MonitorBounds(0, 0, 160, 90),
            new MonitorBounds(0, 0, 160, 90)),
        new(
            "display-2",
            @"\\.\DISPLAY2",
            "Secondary",
            IsPrimary: false,
            IsActive: true,
            new MonitorBounds(160, 0, 160, 90),
            new MonitorBounds(160, 0, 160, 90))
    ];

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        return condition();
    }

    private sealed class MutableMonitorCatalog : IMonitorCatalog
    {
        public MutableMonitorCatalog(IReadOnlyList<MonitorDescriptor> monitors)
        {
            Monitors = monitors;
        }

        public IReadOnlyList<MonitorDescriptor> Monitors { get; set; }

        public IReadOnlyList<MonitorDescriptor> GetMonitors() => Monitors;
    }

    private sealed class RecordingCapture : IFrameCapture
    {
        private readonly ConcurrentDictionary<string, int> _captureCounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly byte[] _pixels = CreatePixels();

        public CapturedFrame Capture(MonitorDescriptor monitor)
        {
            _captureCounts.AddOrUpdate(monitor.StableId, 1, (_, count) => count + 1);
            return new CapturedFrame(
                _pixels.ToArray(),
                new FrameMetadata(DateTimeOffset.UtcNow, 160, 90, monitor.DeviceName),
                monitor);
        }

        public int CaptureCount(string stableId) => _captureCounts.TryGetValue(stableId, out var count) ? count : 0;

        public void Dispose()
        {
        }

        private static byte[] CreatePixels()
        {
            var pixels = new byte[160 * 90 * 4];
            for (var index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = 240;
                pixels[index + 1] = 240;
                pixels[index + 2] = 240;
                pixels[index + 3] = 255;
            }

            return pixels;
        }
    }

    private sealed class FlashingCapture : IFrameCapture
    {
        private readonly string _flashingStableId;
        private readonly ConcurrentDictionary<string, int> _captureCounts = new(StringComparer.OrdinalIgnoreCase);

        public FlashingCapture(string flashingStableId)
        {
            _flashingStableId = flashingStableId;
        }

        public CapturedFrame Capture(MonitorDescriptor monitor)
        {
            var count = _captureCounts.AddOrUpdate(monitor.StableId, 1, (_, current) => current + 1);
            var isBright = string.Equals(monitor.StableId, _flashingStableId, StringComparison.OrdinalIgnoreCase) &&
                count % 2 == 0;

            return new CapturedFrame(
                SolidPixels(isBright ? (byte)255 : (byte)0),
                new FrameMetadata(DateTimeOffset.UtcNow, 160, 90, monitor.DeviceName),
                monitor);
        }

        public void Dispose()
        {
        }

        private static byte[] SolidPixels(byte value)
        {
            var pixels = new byte[160 * 90 * 4];
            for (var index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = value;
                pixels[index + 1] = value;
                pixels[index + 2] = value;
                pixels[index + 3] = 255;
            }

            return pixels;
        }
    }

    private sealed class RecordingOverlay : IOverlayRenderer
    {
        private readonly object _gate = new();

        public IReadOnlyList<string> LastSyncedStableIds { get; private set; } = [];

        public int HideAllCalls { get; private set; }

        public void SyncMonitors(IReadOnlyList<MonitorDescriptor> monitors)
        {
            lock (_gate)
            {
                LastSyncedStableIds = monitors.Select(monitor => monitor.StableId).ToArray();
            }
        }

        public void Show()
        {
        }

        public void SetOpacity(string monitorStableId, double opacity)
        {
        }

        public void HideAll()
        {
            lock (_gate)
            {
                HideAllCalls++;
            }
        }

        public void Dispose()
        {
        }
    }
}
