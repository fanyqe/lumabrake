using System.Collections.Concurrent;
using LumaBrake.Capture;
using LumaBrake.Core;
using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Frames;
using LumaBrake.Domain.Monitors;
using LumaBrake.Domain.Schedules;
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
            new LumaBrakeSettings { ManualDimmingOpacity = 0.20 },
            new MutableMonitorCatalog(monitors));

        controller.EmergencyDisable();

        Assert.Equal(1, overlay.HideAllCalls);
        Assert.False(controller.Settings.Enabled);
        Assert.Equal(0, controller.Settings.ManualDimmingOpacity, precision: 12);
        Assert.All(controller.Snapshot().Monitors, monitor => Assert.Equal(0, monitor.OverlayOpacity));
    }

    [Fact]
    public async Task EmergencyDisable_KeepsRuntimeDisabledUntilUserReenables()
    {
        var monitors = TestMonitors();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            new RecordingOverlay(),
            new LumaBrakeSettings
            {
                ManualDimmingOpacity = 0.20,
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var manualDimmingApplied = await WaitUntilAsync(
            () => controller.Snapshot().Monitors.All(monitor => monitor.OverlayOpacity > 0),
            TimeSpan.FromSeconds(2));
        Assert.True(manualDimmingApplied);

        controller.EmergencyDisable();
        await Task.Delay(TimeSpan.FromMilliseconds(180));

        Assert.False(controller.Settings.Enabled);
        Assert.All(controller.Snapshot().Monitors, monitor => Assert.Equal(0, monitor.OverlayOpacity));
    }

    [Fact]
    public async Task Start_AppliesManualDimmingToEveryDetectedMonitor()
    {
        var monitors = TestMonitors();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            new RecordingOverlay(),
            new LumaBrakeSettings
            {
                ManualDimmingOpacity = 0.25,
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var manualDimmingApplied = await WaitUntilAsync(
            () =>
            {
                var snapshot = controller.Snapshot();
                return snapshot.Monitors.Count == monitors.Length &&
                    snapshot.Monitors.All(monitor =>
                        monitor.ActiveReason == EffectReason.Manual &&
                        Math.Abs(monitor.OverlayOpacity - 0.25) < 0.001);
            },
            TimeSpan.FromSeconds(2));

        Assert.True(manualDimmingApplied);
    }

    [Fact]
    public async Task Start_AddsRepetitiveFlashEventsForOnlyTheFlashingMonitor()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new LocalizedFlashingCapture(monitors[0].StableId),
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                FlashGuard = new FlashDetectorOptions
                {
                    MinimumTransitions = 3,
                    MinimumAffectedTileRatio = 0.005,
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
                    primary.RegionalMaskCount > 0 &&
                    overlay.LastEffect(monitors[0].StableId)?.RegionalMasks.Count > 0 &&
                    secondary.FlashEvents == 0;
            },
            TimeSpan.FromSeconds(3));

        Assert.True(flashDetected);
    }

    [Fact]
    public async Task Start_UsesGlobalFlashFallbackWhenMostTilesAreAffected()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new FlashingCapture(monitors[0].StableId),
            overlay,
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
            new MutableMonitorCatalog([monitors[0]]));

        controller.Start();

        var flashDetected = await WaitUntilAsync(
            () =>
            {
                var effect = overlay.LastEffect(monitors[0].StableId);
                var snapshot = controller.Snapshot().Monitors.Single();
                return snapshot.FlashEvents > 0 &&
                    effect is not null &&
                    effect.GlobalOpacity > 0 &&
                    effect.RegionalMasks.Count == 0;
            },
            TimeSpan.FromSeconds(3));

        Assert.True(flashDetected);
    }

    [Fact]
    public async Task Start_AppliesStaticShadeRegionalMaskForPersistentBrightTile()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new LocalizedStaticBrightCapture(monitors[0].StableId),
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                StaticShade = new StaticShadeOptions
                {
                    MinimumStaticDuration = TimeSpan.FromMilliseconds(100),
                    FullOpacityDuration = TimeSpan.FromMilliseconds(300),
                    MinimumAffectedTileRatio = 0.005,
                    MaximumAffectedTileRatio = 0.50,
                    Hold = TimeSpan.FromMilliseconds(250),
                    Release = TimeSpan.FromMilliseconds(250)
                }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var staticShadeDetected = await WaitUntilAsync(
            () =>
            {
                var snapshot = controller.Snapshot();
                var primary = snapshot.Monitors.Single(monitor => monitor.StableId == monitors[0].StableId);
                var secondary = snapshot.Monitors.Single(monitor => monitor.StableId == monitors[1].StableId);
                var effect = overlay.LastEffect(monitors[0].StableId);
                return primary.ActiveReason == EffectReason.StaticShade &&
                    primary.RegionalMaskCount > 0 &&
                    effect is not null &&
                    effect.RegionalMasks.Any(mask => mask.Reason == EffectReason.StaticShade) &&
                    secondary.RegionalMaskCount == 0;
            },
            TimeSpan.FromSeconds(3));

        Assert.True(staticShadeDetected);
    }

    [Fact]
    public async Task Start_AppliesAdaptiveDimmingForPersistentBrightScene()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                AdaptiveContent = new AdaptiveContentOptions
                {
                    MinimumBrightDuration = TimeSpan.FromMilliseconds(100),
                    MinimumAverageLuminance = 0.30,
                    MinimumPercentile95Luminance = 0.50,
                    MinimumBrightPixelRatio = 0.20,
                    Hold = TimeSpan.FromMilliseconds(250),
                    Release = TimeSpan.FromMilliseconds(250)
                }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var adaptiveDetected = await WaitUntilAsync(
            () =>
            {
                var snapshot = controller.Snapshot();
                return snapshot.Monitors.Count == monitors.Length &&
                    snapshot.Monitors.All(monitor =>
                        monitor.ActiveReason == EffectReason.AdaptiveContent &&
                        monitor.OverlayOpacity > 0 &&
                        overlay.LastEffect(monitor.StableId)?.GlobalOpacity > 0);
            },
            TimeSpan.FromSeconds(3));

        Assert.True(adaptiveDetected);
    }

    [Fact]
    public async Task Start_DimsOnlySecondaryMonitorWhenFrameStaysInactive()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions
                {
                    MinimumInactiveDuration = TimeSpan.FromMilliseconds(100),
                    MaximumFrameGap = TimeSpan.FromMilliseconds(500),
                    Hold = TimeSpan.FromMilliseconds(250),
                    Release = TimeSpan.FromMilliseconds(250),
                    Opacity = 0.22
                }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var inactiveDetected = await WaitUntilAsync(
            () =>
            {
                var snapshot = controller.Snapshot();
                var primary = snapshot.Monitors.Single(monitor => monitor.StableId == monitors[0].StableId);
                var secondary = snapshot.Monitors.Single(monitor => monitor.StableId == monitors[1].StableId);
                return primary.ActiveReason == EffectReason.None &&
                    secondary.ActiveReason == EffectReason.InactiveMonitor &&
                    secondary.OverlayOpacity > 0 &&
                    overlay.LastEffect(monitors[1].StableId)?.GlobalReason == EffectReason.InactiveMonitor;
            },
            TimeSpan.FromSeconds(3));

        Assert.True(inactiveDetected);
    }

    [Fact]
    public async Task Start_DimsOnlyMonitorsOutsideFocusedMonitor()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false },
                FocusDimming = new FocusDimmingOptions
                {
                    Enabled = true,
                    Opacity = 0.17,
                    Hold = TimeSpan.FromMilliseconds(250),
                    Release = TimeSpan.FromMilliseconds(250)
                }
            },
            new MutableMonitorCatalog(monitors),
            new FixedFocusedMonitorProvider(monitors[0].StableId));

        controller.Start();

        var focusDimmingApplied = await WaitUntilAsync(
            () =>
            {
                var snapshot = controller.Snapshot();
                var primary = snapshot.Monitors.Single(monitor => monitor.StableId == monitors[0].StableId);
                var secondary = snapshot.Monitors.Single(monitor => monitor.StableId == monitors[1].StableId);
                return primary.ActiveReason == EffectReason.None &&
                    secondary.ActiveReason == EffectReason.FocusDimming &&
                    Math.Abs(secondary.OverlayOpacity - 0.17) < 0.001 &&
                    overlay.LastEffect(monitors[1].StableId)?.GlobalReason == EffectReason.FocusDimming;
            },
            TimeSpan.FromSeconds(2));

        Assert.True(focusDimmingApplied);
    }

    [Fact]
    public async Task Start_SkipsFocusDimmingWhenFocusedMonitorIsUnknown()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false },
                FocusDimming = new FocusDimmingOptions { Enabled = true, Opacity = 0.17 }
            },
            new MutableMonitorCatalog(monitors),
            new FixedFocusedMonitorProvider("missing-monitor"));

        controller.Start();

        var framesAnalyzed = await WaitUntilAsync(
            () => controller.Snapshot().FramesAnalyzed >= monitors.Length,
            TimeSpan.FromSeconds(2));

        Assert.True(framesAnalyzed);
        Assert.All(controller.Snapshot().Monitors, monitor =>
        {
            Assert.Equal(EffectReason.None, monitor.ActiveReason);
            Assert.Equal(0, monitor.OverlayOpacity);
        });
    }

    [Fact]
    public async Task Start_SkipsFocusDimmingWhenFocusedMonitorProviderFails()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false },
                FocusDimming = new FocusDimmingOptions { Enabled = true, Opacity = 0.17 }
            },
            new MutableMonitorCatalog(monitors),
            ThrowingFocusedMonitorProvider.Instance);

        controller.Start();

        var framesAnalyzed = await WaitUntilAsync(
            () => controller.Snapshot().FramesAnalyzed >= monitors.Length,
            TimeSpan.FromSeconds(2));

        Assert.True(framesAnalyzed);
        Assert.True(controller.Settings.Enabled);
        Assert.All(controller.Snapshot().Monitors, monitor =>
        {
            Assert.Equal(EffectReason.None, monitor.ActiveReason);
            Assert.Equal(0, monitor.OverlayOpacity);
        });
        Assert.Equal(1, overlay.HideAllCalls);
    }

    [Fact]
    public async Task Start_ReleasesFocusDimmingWhenFocusMovesToAnotherMonitor()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        var focusedMonitorProvider = new MutableFocusedMonitorProvider(monitors[0].StableId);
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false },
                FocusDimming = new FocusDimmingOptions
                {
                    Enabled = true,
                    Opacity = 0.17,
                    Hold = TimeSpan.FromMilliseconds(20),
                    Release = TimeSpan.FromMilliseconds(40)
                }
            },
            new MutableMonitorCatalog(monitors),
            focusedMonitorProvider);

        controller.Start();

        var secondaryDimmed = await WaitUntilAsync(
            () => controller.Snapshot().Monitors.Single(monitor => monitor.StableId == monitors[1].StableId).ActiveReason == EffectReason.FocusDimming,
            TimeSpan.FromSeconds(2));
        Assert.True(secondaryDimmed);

        focusedMonitorProvider.FocusedStableId = monitors[1].StableId;

        var focusMoved = await WaitUntilAsync(
            () =>
            {
                var snapshot = controller.Snapshot();
                var primary = snapshot.Monitors.Single(monitor => monitor.StableId == monitors[0].StableId);
                var secondary = snapshot.Monitors.Single(monitor => monitor.StableId == monitors[1].StableId);
                return primary.ActiveReason == EffectReason.FocusDimming &&
                    secondary.ActiveReason == EffectReason.None &&
                    primary.OverlayOpacity > 0 &&
                    secondary.OverlayOpacity == 0;
            },
            TimeSpan.FromSeconds(3));

        Assert.True(focusMoved);
    }

    [Fact]
    public async Task Start_RendersTintWithoutAutomaticDimming()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false },
                Tint = new TintOptions { Enabled = true, Red = 255, Green = 214, Blue = 170, Opacity = 0.14 }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var tintRendered = await WaitUntilAsync(
            () => monitors.All(monitor =>
            {
                var effect = overlay.LastEffect(monitor.StableId);
                return effect is not null &&
                    effect.GlobalOpacity == 0 &&
                    effect.GlobalReason == EffectReason.None &&
                    effect.Tint.Enabled &&
                    Math.Abs(effect.Tint.Opacity - 0.14) < 0.001;
            }),
            TimeSpan.FromSeconds(2));

        Assert.True(tintRendered);
    }

    [Fact]
    public async Task Start_RendersMonitorTintOnlyOnMatchingMonitor()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false },
                MonitorTints = new MonitorTintOptions
                {
                    Entries =
                    [
                        new MonitorTintOverride
                        {
                            MonitorId = monitors[1].StableId,
                            Tint = new TintOptions { Enabled = true, Red = 255, Green = 244, Blue = 230, Opacity = 0.22 }
                        }
                    ]
                }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var tintRendered = await WaitUntilAsync(
            () =>
            {
                var primary = overlay.LastEffect(monitors[0].StableId);
                var secondary = overlay.LastEffect(monitors[1].StableId);
                return primary is not null &&
                    secondary is not null &&
                    !primary.Tint.Enabled &&
                    secondary.Tint.Enabled &&
                    Math.Abs(secondary.Tint.Opacity - 0.22) < 0.001;
            },
            TimeSpan.FromSeconds(2));

        Assert.True(tintRendered);
    }

    [Fact]
    public async Task Start_UsesScheduleTintBeforeMonitorTint()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false },
                MonitorTints = new MonitorTintOptions
                {
                    Entries =
                    [
                        new MonitorTintOverride
                        {
                            MonitorId = monitors[1].StableId,
                            Tint = new TintOptions { Enabled = true, Opacity = 0.22 }
                        }
                    ]
                },
                Schedules = new ScheduleOptions
                {
                    Enabled = true,
                    Entries =
                    [
                        ActiveSchedule(0.18, new TintOptions { Enabled = true, Red = 255, Green = 214, Blue = 170, Opacity = 0.16 })
                    ]
                }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var scheduleTintRendered = await WaitUntilAsync(
            () => monitors.All(monitor =>
            {
                var effect = overlay.LastEffect(monitor.StableId);
                return effect is not null &&
                    effect.Tint.Enabled &&
                    Math.Abs(effect.Tint.Opacity - 0.16) < 0.001;
            }),
            TimeSpan.FromSeconds(2));

        Assert.True(scheduleTintRendered);
    }

    [Fact]
    public async Task Start_AppliesActiveScheduleAsRuntimeEffect()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false },
                Schedules = new ScheduleOptions
                {
                    Enabled = true,
                    Entries =
                    [
                        ActiveSchedule(0.18, new TintOptions { Enabled = true, Red = 255, Green = 214, Blue = 170, Opacity = 0.16 })
                    ]
                }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var scheduledEffectApplied = await WaitUntilAsync(
            () => monitors.All(monitor =>
            {
                var effect = overlay.LastEffect(monitor.StableId);
                return effect is not null &&
                    effect.GlobalReason == EffectReason.Scheduled &&
                    Math.Abs(effect.GlobalOpacity - 0.18) < 0.001 &&
                    effect.Tint.Enabled &&
                    Math.Abs(effect.Tint.Opacity - 0.16) < 0.001;
            }),
            TimeSpan.FromSeconds(2));

        Assert.True(scheduledEffectApplied);
    }

    [Fact]
    public async Task Start_DoesNotDoubleApplyScheduleWhenManualDimmingAlreadyMatchesTarget()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                ManualDimmingOpacity = 0.18,
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false },
                Schedules = new ScheduleOptions
                {
                    Enabled = true,
                    Entries = [ActiveSchedule(0.18, new TintOptions { Enabled = false })]
                }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var opacityStable = await WaitUntilAsync(
            () => monitors.All(monitor =>
            {
                var effect = overlay.LastEffect(monitor.StableId);
                return effect is not null &&
                    effect.GlobalReason == EffectReason.Manual &&
                    Math.Abs(effect.GlobalOpacity - 0.18) < 0.001;
            }),
            TimeSpan.FromSeconds(2));

        Assert.True(opacityStable);
    }

    [Fact]
    public async Task EmergencyDisable_PreventsActiveScheduleFromReturning()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false },
                Schedules = new ScheduleOptions
                {
                    Enabled = true,
                    Entries =
                    [
                        ActiveSchedule(0.18, new TintOptions { Enabled = true, Opacity = 0.16 })
                    ]
                }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var scheduledEffectApplied = await WaitUntilAsync(
            () => overlay.LastEffect(monitors[0].StableId)?.GlobalReason == EffectReason.Scheduled,
            TimeSpan.FromSeconds(2));
        Assert.True(scheduledEffectApplied);

        controller.EmergencyDisable();
        await Task.Delay(TimeSpan.FromMilliseconds(180));

        Assert.False(controller.Settings.Enabled);
        Assert.All(controller.Snapshot().Monitors, monitor => Assert.Equal(0, monitor.OverlayOpacity));
        Assert.Equal(0, controller.Settings.ManualDimmingOpacity, precision: 12);
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

    private static ScheduledEffect ActiveSchedule(double opacity, TintOptions tint)
    {
        return new ScheduledEffect
        {
            Id = "test",
            Name = "Test",
            DayMask = ScheduleDayMask.EveryDay,
            StartMinuteOfDay = 0,
            EndMinuteOfDay = 1439,
            ManualDimmingOpacity = opacity,
            Tint = tint
        };
    }

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

    private sealed class FixedFocusedMonitorProvider : IFocusedMonitorProvider
    {
        private readonly string? _focusedStableId;

        public FixedFocusedMonitorProvider(string? focusedStableId)
        {
            _focusedStableId = focusedStableId;
        }

        public string? FocusedMonitorStableId(IReadOnlyList<MonitorDescriptor> monitors) => _focusedStableId;
    }

    private sealed class MutableFocusedMonitorProvider : IFocusedMonitorProvider
    {
        public MutableFocusedMonitorProvider(string? focusedStableId)
        {
            FocusedStableId = focusedStableId;
        }

        public string? FocusedStableId { get; set; }

        public string? FocusedMonitorStableId(IReadOnlyList<MonitorDescriptor> monitors) => FocusedStableId;
    }

    private sealed class ThrowingFocusedMonitorProvider : IFocusedMonitorProvider
    {
        public static readonly ThrowingFocusedMonitorProvider Instance = new();

        public string? FocusedMonitorStableId(IReadOnlyList<MonitorDescriptor> monitors) =>
            throw new InvalidOperationException("Focus lookup failed.");
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

    private sealed class LocalizedFlashingCapture : IFrameCapture
    {
        private readonly string _flashingStableId;
        private readonly ConcurrentDictionary<string, int> _captureCounts = new(StringComparer.OrdinalIgnoreCase);

        public LocalizedFlashingCapture(string flashingStableId)
        {
            _flashingStableId = flashingStableId;
        }

        public CapturedFrame Capture(MonitorDescriptor monitor)
        {
            var count = _captureCounts.AddOrUpdate(monitor.StableId, 1, (_, current) => current + 1);
            var isBright = string.Equals(monitor.StableId, _flashingStableId, StringComparison.OrdinalIgnoreCase) &&
                count % 2 == 0;

            return new CapturedFrame(
                LocalizedPixels(isBright),
                new FrameMetadata(DateTimeOffset.UtcNow, 160, 90, monitor.DeviceName),
                monitor);
        }

        public void Dispose()
        {
        }

        private static byte[] LocalizedPixels(bool isBright)
        {
            var pixels = new byte[160 * 90 * 4];
            for (var index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = 0;
                pixels[index + 1] = 0;
                pixels[index + 2] = 0;
                pixels[index + 3] = 255;
            }

            if (!isBright)
            {
                return pixels;
            }

            for (var y = 0; y < 10; y++)
            {
                for (var x = 0; x < 10; x++)
                {
                    var offset = ((y * 160) + x) * 4;
                    pixels[offset] = 255;
                    pixels[offset + 1] = 255;
                    pixels[offset + 2] = 255;
                    pixels[offset + 3] = 255;
                }
            }

            return pixels;
        }
    }

    private sealed class LocalizedStaticBrightCapture : IFrameCapture
    {
        private readonly string _brightStableId;

        public LocalizedStaticBrightCapture(string brightStableId)
        {
            _brightStableId = brightStableId;
        }

        public CapturedFrame Capture(MonitorDescriptor monitor)
        {
            var isBright = string.Equals(monitor.StableId, _brightStableId, StringComparison.OrdinalIgnoreCase);
            return new CapturedFrame(
                LocalizedPixels(isBright),
                new FrameMetadata(DateTimeOffset.UtcNow, 160, 90, monitor.DeviceName),
                monitor);
        }

        public void Dispose()
        {
        }

        private static byte[] LocalizedPixels(bool isBright)
        {
            var pixels = new byte[160 * 90 * 4];
            for (var index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = 0;
                pixels[index + 1] = 0;
                pixels[index + 2] = 0;
                pixels[index + 3] = 255;
            }

            if (!isBright)
            {
                return pixels;
            }

            for (var y = 0; y < 10; y++)
            {
                for (var x = 0; x < 10; x++)
                {
                    var offset = ((y * 160) + x) * 4;
                    pixels[offset] = 255;
                    pixels[offset + 1] = 255;
                    pixels[offset + 2] = 255;
                    pixels[offset + 3] = 255;
                }
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
        private readonly Dictionary<string, FinalEffectState> _effects = new(StringComparer.OrdinalIgnoreCase);

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

        public void SetEffect(string monitorStableId, FinalEffectState effect)
        {
            lock (_gate)
            {
                _effects[monitorStableId] = effect;
            }
        }

        public void HideAll()
        {
            lock (_gate)
            {
                HideAllCalls++;
            }
        }

        public FinalEffectState? LastEffect(string monitorStableId)
        {
            lock (_gate)
            {
                return _effects.GetValueOrDefault(monitorStableId);
            }
        }

        public void Dispose()
        {
        }
    }
}
