using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LumaBrake.Capture;
using LumaBrake.Core;
using LumaBrake.Diagnostics;
using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Color;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Frames;
using LumaBrake.Domain.Monitors;
using LumaBrake.Domain.Rules;
using LumaBrake.Domain.Schedules;
using LumaBrake.Persistence;
using LumaBrake.Rendering;

namespace LumaBrake.Core.Tests;

public sealed class LumaBrakeControllerTests
{
    [Fact]
    public void Constructor_BuildsPipelineSnapshotsForEveryDetectedMonitor()
    {
        var monitors = TestMonitors(primaryAdvancedColorEnabled: true);
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
        Assert.Contains(snapshot.Monitors, monitor => monitor.StableId == monitors[0].StableId && monitor.AdvancedColor.IsEnabled);
    }

    [Fact]
    public void Constructor_HandlesRecoverableMonitorEnumerationFailure()
    {
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings(),
            new ThrowingMonitorCatalog(new COMException("Display configuration unavailable.")));

        var snapshot = controller.Snapshot();

        Assert.Equal(0, snapshot.MonitorCount);
        Assert.Empty(snapshot.Monitors);
        Assert.Equal("Monitor detection failed.", snapshot.MonitorStatus);
        Assert.Empty(overlay.LastSyncedStableIds);
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
    public void RefreshMonitors_ClearsPipelinesAfterRecoverableMonitorEnumerationFailure()
    {
        var monitors = TestMonitors();
        var catalog = new MutableMonitorCatalog(monitors);
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings(),
            catalog);

        catalog.Failure = new ObjectDisposedException("Display topology");
        controller.RefreshMonitors();

        var snapshot = controller.Snapshot();

        Assert.Equal(0, snapshot.MonitorCount);
        Assert.Empty(snapshot.Monitors);
        Assert.Equal("Monitor detection failed.", snapshot.MonitorStatus);
        Assert.Empty(overlay.LastSyncedStableIds);
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
    public async Task Start_CapturesDetectedMonitorsConcurrently()
    {
        var monitors = TestMonitors();
        using var capture = new CoordinatedFirstPassCapture(monitors.Length);
        using var controller = new LumaBrakeController(
            capture,
            new RecordingOverlay(),
            new LumaBrakeSettings(),
            new MutableMonitorCatalog(monitors),
            analysisInterval: TimeSpan.FromMilliseconds(20));

        controller.Start();
        var bothMonitorsEnteredFirstPass = capture.WaitForFirstPass(TimeSpan.FromSeconds(3));
        capture.Release();

        Assert.True(
            bothMonitorsEnteredFirstPass,
            string.Join(
                Environment.NewLine,
                monitors.Select(monitor => $"{monitor.StableId} captures={capture.CaptureCount(monitor.StableId)}")));
        var snapshotUpdated = await WaitUntilAsync(
            () => controller.Snapshot().FramesAnalyzed >= monitors.Length,
            TimeSpan.FromSeconds(2));
        Assert.True(snapshotUpdated);
    }

    [Fact]
    public async Task Start_AutomaticallyAddsMonitorWhenCatalogChanges()
    {
        var monitors = TestMonitors();
        var catalog = new MutableMonitorCatalog([monitors[0]]);
        var capture = new RecordingCapture();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            capture,
            overlay,
            new LumaBrakeSettings(),
            catalog,
            monitorRefreshInterval: TimeSpan.FromMilliseconds(80));

        controller.Start();

        var initialMonitorAnalyzed = await WaitUntilAsync(
            () => capture.CaptureCount(monitors[0].StableId) > 0,
            TimeSpan.FromSeconds(2));
        Assert.True(initialMonitorAnalyzed);

        catalog.Monitors = monitors;

        var addedMonitorSynced = await WaitUntilAsync(
            () =>
            {
                var snapshot = controller.Snapshot();
                return snapshot.MonitorCount == 2 &&
                    snapshot.Monitors.Any(monitor => monitor.StableId == monitors[1].StableId) &&
                    overlay.LastSyncedStableIds.Contains(monitors[1].StableId) &&
                    capture.CaptureCount(monitors[1].StableId) > 0;
            },
            TimeSpan.FromSeconds(3));

        Assert.True(addedMonitorSynced);
    }

    [Fact]
    public async Task Start_AutomaticallyRemovesMonitorWhenCatalogChanges()
    {
        var monitors = TestMonitors();
        var catalog = new MutableMonitorCatalog(monitors);
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings(),
            catalog,
            monitorRefreshInterval: TimeSpan.FromMilliseconds(80));

        controller.Start();

        var bothMonitorsPresent = await WaitUntilAsync(
            () => controller.Snapshot().MonitorCount == 2,
            TimeSpan.FromSeconds(2));
        Assert.True(bothMonitorsPresent);

        catalog.Monitors = [monitors[0]];

        var removedMonitorSynced = await WaitUntilAsync(
            () =>
            {
                var snapshot = controller.Snapshot();
                return snapshot.MonitorCount == 1 &&
                    snapshot.Monitors.Single().StableId == monitors[0].StableId &&
                    overlay.LastSyncedStableIds.SequenceEqual([monitors[0].StableId]);
            },
            TimeSpan.FromSeconds(3));

        Assert.True(removedMonitorSynced);
    }

    [Fact]
    public async Task RefreshMonitors_ClearsAutomaticEffectsWhenExistingMonitorDescriptorChanges()
    {
        var monitors = TestMonitors();
        var capture = new MutableBrightnessCapture(240);
        var catalog = new MutableMonitorCatalog([monitors[0]]);
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            capture,
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                FlashGuard = new FlashDetectorOptions { Enabled = false },
                StaticShade = new StaticShadeOptions { Enabled = false },
                AdaptiveContent = new AdaptiveContentOptions
                {
                    MinimumBrightDuration = TimeSpan.FromMilliseconds(50),
                    MinimumAverageLuminance = 0.30,
                    MinimumPercentile95Luminance = 0.50,
                    MinimumBrightPixelRatio = 0.20,
                    Hold = TimeSpan.FromSeconds(5),
                    Release = TimeSpan.FromSeconds(5)
                },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false }
            },
            catalog);

        controller.Start();

        var adaptiveApplied = await WaitUntilAsync(
            () =>
            {
                var monitor = controller.Snapshot().Monitors.Single();
                return monitor.ActiveReason == EffectReason.AdaptiveContent && monitor.OverlayOpacity > 0;
            },
            TimeSpan.FromSeconds(3));
        Assert.True(adaptiveApplied);

        capture.SetBrightness(0);
        catalog.Monitors =
        [
            monitors[0] with
            {
                Bounds = new MonitorBounds(0, 0, 320, 180),
                WorkArea = new MonitorBounds(0, 0, 320, 180)
            }
        ];

        controller.RefreshMonitors();

        var cleared = await WaitUntilAsync(
            () =>
            {
                var monitor = controller.Snapshot().Monitors.Single();
                return monitor.ActiveReason == EffectReason.None &&
                    Math.Abs(monitor.OverlayOpacity) < 0.001 &&
                    (overlay.LastEffect(monitors[0].StableId)?.GlobalOpacity ?? 0) < 0.001;
            },
            TimeSpan.FromSeconds(1));

        Assert.True(cleared);
    }

    [Fact]
    public void EmergencyDisable_HidesEveryOverlayAndClearsPipelineOpacity()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                ManualDimmingOpacity = 0.20,
                MonitorManualDimming = new MonitorManualDimmingOptions
                {
                    Entries = [new MonitorManualDimmingOverride { MonitorId = monitors[1].StableId, Opacity = 0.30 }]
                }
            },
            new MutableMonitorCatalog(monitors));

        controller.EmergencyDisable();

        Assert.Equal(1, overlay.HideAllCalls);
        Assert.False(controller.Settings.Enabled);
        Assert.Equal(0, controller.Settings.ManualDimmingOpacity, precision: 12);
        Assert.Empty(controller.Settings.MonitorManualDimming.Entries);
        Assert.All(controller.Snapshot().Monitors, monitor => Assert.Equal(0, monitor.OverlayOpacity));
    }

    [Fact]
    public void EmergencyDisable_WhenHideAllThrows_DisablesRuntimeAndPublishesClearSnapshot()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay { ThrowOnHideAll = true };
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                ManualDimmingOpacity = 0.20,
                MonitorManualDimming = new MonitorManualDimmingOptions
                {
                    Entries = [new MonitorManualDimmingOverride { MonitorId = monitors[1].StableId, Opacity = 0.30 }]
                }
            },
            new MutableMonitorCatalog(monitors));
        var snapshotPublished = false;
        controller.SnapshotUpdated += (_, _) => snapshotPublished = true;

        controller.EmergencyDisable();

        var snapshot = controller.Snapshot();
        Assert.Equal(1, overlay.HideAllCalls);
        Assert.Equal(1, overlay.HideFailures);
        Assert.False(controller.Settings.Enabled);
        Assert.Equal(0, controller.Settings.ManualDimmingOpacity, precision: 12);
        Assert.Empty(controller.Settings.MonitorManualDimming.Entries);
        Assert.True(snapshotPublished);
        Assert.All(snapshot.Monitors, monitor =>
        {
            Assert.Equal(EffectReason.None, monitor.ActiveReason);
            Assert.Equal(0, monitor.OverlayOpacity);
        });
    }

    [Fact]
    public void ApplySettings_Disabled_WhenHideAllThrows_PublishesClearSnapshot()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay { ThrowOnHideAll = true };
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings { ManualDimmingOpacity = 0.35 },
            new MutableMonitorCatalog(monitors));
        var publishedSnapshots = 0;
        controller.SnapshotUpdated += (_, _) => publishedSnapshots++;

        controller.ApplySettings(controller.Settings with { Enabled = false });

        var snapshot = controller.Snapshot();
        Assert.Equal(1, overlay.HideAllCalls);
        Assert.Equal(1, overlay.HideFailures);
        Assert.False(controller.Settings.Enabled);
        Assert.True(publishedSnapshots > 0);
        Assert.All(snapshot.Monitors, monitor =>
        {
            Assert.Equal(EffectReason.None, monitor.ActiveReason);
            Assert.Equal(0, monitor.OverlayOpacity);
        });
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
        await Task.Delay(TimeSpan.FromMilliseconds(180), TestContext.Current.CancellationToken);

        Assert.False(controller.Settings.Enabled);
        Assert.All(controller.Snapshot().Monitors, monitor => Assert.Equal(0, monitor.OverlayOpacity));
    }

    [Fact]
    public async Task Start_RecoversRecoverableCaptureFailureWithoutGlobalLoopRecovery()
    {
        var monitors = TestMonitors();
        var capture = new FailsFirstCapture();
        using var controller = new LumaBrakeController(
            capture,
            new RecordingOverlay(),
            new LumaBrakeSettings
            {
                ManualDimmingOpacity = 0.25,
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var recovered = await WaitUntilAsync(
            () =>
            {
                var snapshot = controller.Snapshot();
                return controller.Settings.Enabled &&
                    Math.Abs(controller.Settings.ManualDimmingOpacity - 0.25) < 0.001 &&
                    snapshot.TransientFailureCount == 0 &&
                    snapshot.CaptureFailureEvents == 1 &&
                    snapshot.LastCaptureFailureAt is not null &&
                    !snapshot.HasActiveCaptureFailure &&
                    snapshot.Monitors.Count == monitors.Length &&
                    snapshot.Monitors.All(monitor =>
                        !monitor.IsCaptureFailureActive &&
                        monitor.ActiveReason == EffectReason.Manual &&
                        Math.Abs(monitor.OverlayOpacity - 0.25) < 0.001);
            },
            TimeSpan.FromSeconds(4));

        Assert.True(recovered);
        Assert.True(capture.Attempts >= monitors.Length + 1);
    }

    [Fact]
    public async Task Start_RecoversFrameCaptureDeviceLossWithoutLeavingOverlayActive()
    {
        var monitors = TestMonitors();
        var capture = new FailsFirstCapture(new FrameCaptureDeviceLostException("Capture device was lost."));
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            capture,
            overlay,
            new LumaBrakeSettings
            {
                ManualDimmingOpacity = 0.25,
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var recovered = await WaitUntilAsync(
            () =>
            {
                var snapshot = controller.Snapshot();
                return controller.Settings.Enabled &&
                    snapshot.TransientFailureCount == 0 &&
                    snapshot.CaptureFailureEvents == 1 &&
                    snapshot.LastCaptureFailureAt is not null &&
                    !snapshot.HasActiveCaptureFailure &&
                    snapshot.Monitors.Count == monitors.Length &&
                    snapshot.Monitors.All(monitor =>
                        !monitor.IsCaptureFailureActive &&
                        monitor.ActiveReason == EffectReason.Manual &&
                        Math.Abs(monitor.OverlayOpacity - 0.25) < 0.001);
            },
            TimeSpan.FromSeconds(4));

        Assert.True(recovered);
        Assert.True(capture.Attempts >= monitors.Length + 1);
    }

    [Fact]
    public async Task Start_PreservesLastSafeOverlayDuringBriefCaptureFailure()
    {
        var monitors = TestMonitors();
        var capture = new BurstFailureCapture(240);
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            capture,
            overlay,
            ManualOnlySettings(),
            new MutableMonitorCatalog([monitors[0]]),
            analysisInterval: TimeSpan.FromMilliseconds(20));

        controller.Start();

        var manualApplied = await WaitUntilAsync(
            () => overlay.LastEffect(monitors[0].StableId)?.GlobalReason == EffectReason.Manual,
            TimeSpan.FromSeconds(2));
        Assert.True(manualApplied);

        overlay.ClearEffectHistory();
        capture.FailNext(1);

        var failureObserved = await WaitUntilAsync(
            () => controller.Snapshot().CaptureFailureEvents >= 1 && capture.FailuresTriggered >= 1,
            TimeSpan.FromSeconds(2));

        Assert.True(failureObserved);
        Assert.DoesNotContain(
            overlay.EffectsFor(monitors[0].StableId),
            effect => effect.GlobalReason == EffectReason.None && effect.GlobalOpacity <= 0.001);
        Assert.Equal(EffectReason.Manual, overlay.LastEffect(monitors[0].StableId)?.GlobalReason);
    }

    [Fact]
    public async Task Start_ClearsOverlayAfterSustainedCaptureFailure()
    {
        var monitors = TestMonitors();
        var capture = new BurstFailureCapture(240);
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            capture,
            overlay,
            ManualOnlySettings(),
            new MutableMonitorCatalog([monitors[0]]),
            analysisInterval: TimeSpan.FromMilliseconds(20));

        controller.Start();

        var manualApplied = await WaitUntilAsync(
            () => overlay.LastEffect(monitors[0].StableId)?.GlobalReason == EffectReason.Manual,
            TimeSpan.FromSeconds(2));
        Assert.True(manualApplied);

        overlay.ClearEffectHistory();
        capture.FailNext(3);

        var clearApplied = await WaitUntilAsync(
            () => capture.FailuresTriggered >= 3 &&
                overlay.EffectsFor(monitors[0].StableId).Any(effect =>
                    effect.GlobalReason == EffectReason.None && effect.GlobalOpacity <= 0.001),
            TimeSpan.FromSeconds(2));

        Assert.True(clearApplied);
    }

    [Fact]
    public async Task Start_ContinuesHealthyMonitorWhenOneMonitorCaptureFails()
    {
        var monitors = TestMonitors();
        var capture = new FailingMonitorCapture(monitors[0].StableId);
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            capture,
            overlay,
            new LumaBrakeSettings
            {
                ManualDimmingOpacity = 0.25,
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var isolated = await WaitUntilAsync(
            () =>
            {
                var snapshot = controller.Snapshot();
                var failed = snapshot.Monitors.Single(monitor => monitor.StableId == monitors[0].StableId);
                var healthy = snapshot.Monitors.Single(monitor => monitor.StableId == monitors[1].StableId);
                return snapshot.TransientFailureCount == 0 &&
                    snapshot.CaptureFailureEvents > 0 &&
                    snapshot.HasActiveCaptureFailure &&
                    failed.IsCaptureFailureActive &&
                    failed.ActiveReason == EffectReason.None &&
                    Math.Abs(failed.OverlayOpacity) < 0.001 &&
                    healthy.FramesAnalyzed > 0 &&
                    !healthy.IsCaptureFailureActive &&
                    healthy.ActiveReason == EffectReason.Manual &&
                    Math.Abs(healthy.OverlayOpacity - 0.25) < 0.001;
            },
            TimeSpan.FromSeconds(3));

        var finalSnapshot = controller.Snapshot();
        Assert.True(
            isolated,
            $"transient={finalSnapshot.TransientFailureCount}, captureFailures={finalSnapshot.CaptureFailureEvents}, activeFailure={finalSnapshot.HasActiveCaptureFailure}, effects={string.Join(',', finalSnapshot.Monitors.Select(monitor => $"{monitor.StableId}:{monitor.ActiveReason}:{monitor.OverlayOpacity:0.000}:{monitor.IsCaptureFailureActive}"))}");
        Assert.True(capture.CaptureCount(monitors[1].StableId) > 0);
    }

    [Fact]
    public async Task Start_UsesTransientRecoveryWhenCaptureFailureClearFails()
    {
        var monitors = TestMonitors();
        var overlay = new TransientRenderFailureOverlay();
        using var controller = new LumaBrakeController(
            new FailingMonitorCapture(monitors[0].StableId),
            overlay,
            new LumaBrakeSettings
            {
                ManualDimmingOpacity = 0.25,
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var recovered = await WaitUntilAsync(
            () =>
            {
                var snapshot = controller.Snapshot();
                return snapshot.CaptureFailureEvents > 0 &&
                    snapshot.TransientFailureCount > 0 &&
                    overlay.HideFailures == 1;
            },
            TimeSpan.FromSeconds(5));

        Assert.True(recovered);
    }

    [Fact]
    public async Task Start_TreatsMalformedCapturedFrameAsTransientLoopFailure()
    {
        var monitors = TestMonitors();
        using var controller = new LumaBrakeController(
            new MalformedFrameCapture(),
            new RecordingOverlay(),
            new LumaBrakeSettings
            {
                ManualDimmingOpacity = 0.25,
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var recovered = await WaitUntilAsync(
            () =>
            {
                var snapshot = controller.Snapshot();
                return snapshot.TransientFailureCount > 0 &&
                    snapshot.CaptureFailureEvents == 0 &&
                    snapshot.Monitors.All(monitor =>
                        !monitor.IsCaptureFailureActive &&
                        monitor.ActiveReason == EffectReason.None &&
                        monitor.OverlayOpacity < 0.001);
            },
            TimeSpan.FromSeconds(4));

        Assert.True(recovered);
    }

    [Fact]
    public async Task Start_RecoversWhenOverlayHideFailsDuringTransientRenderRecovery()
    {
        var monitors = TestMonitors();
        var overlay = new TransientRenderFailureOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                ManualDimmingOpacity = 0.25,
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var recovered = await WaitUntilAsync(
            () =>
            {
                var snapshot = controller.Snapshot();
                return controller.Settings.Enabled &&
                    snapshot.TransientFailureCount >= 1 &&
                    overlay.HideFailures == 1 &&
                    overlay.SetEffectAttempts > 1 &&
                    snapshot.Monitors.Count == monitors.Length &&
                    snapshot.Monitors.All(monitor =>
                        monitor.ActiveReason == EffectReason.Manual &&
                        monitor.OverlayOpacity > 0);
            },
            TimeSpan.FromSeconds(8));

        var finalSnapshot = controller.Snapshot();
        Assert.True(
            recovered,
            $"transient={finalSnapshot.TransientFailureCount}, hideFailures={overlay.HideFailures}, setAttempts={overlay.SetEffectAttempts}, reasons={string.Join(',', finalSnapshot.Monitors.Select(monitor => monitor.ActiveReason))}, opacities={string.Join(',', finalSnapshot.Monitors.Select(monitor => monitor.OverlayOpacity.ToString("0.000")))}");
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
    public async Task Start_AppliesMonitorManualDimmingOverrideOnlyOnMatchingMonitor()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                ManualDimmingOpacity = 0.25,
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false },
                MonitorManualDimming = new MonitorManualDimmingOptions
                {
                    Entries =
                    [
                        new MonitorManualDimmingOverride { MonitorId = monitors[1].StableId, Opacity = 0.10 }
                    ]
                }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var manualDimmingApplied = await WaitUntilAsync(
            () =>
            {
                var primary = overlay.LastEffect(monitors[0].StableId);
                var secondary = overlay.LastEffect(monitors[1].StableId);
                return primary is not null &&
                    secondary is not null &&
                    primary.GlobalReason == EffectReason.Manual &&
                    secondary.GlobalReason == EffectReason.Manual &&
                    Math.Abs(primary.GlobalOpacity - 0.25) < 0.001 &&
                    Math.Abs(secondary.GlobalOpacity - 0.10) < 0.001;
            },
            TimeSpan.FromSeconds(2));

        Assert.True(manualDimmingApplied);
    }

    [Fact]
    public async Task Start_AllowsMonitorManualDimmingOverrideToDisableGlobalDimming()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                ManualDimmingOpacity = 0.25,
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false },
                MonitorManualDimming = new MonitorManualDimmingOptions
                {
                    Entries =
                    [
                        new MonitorManualDimmingOverride { MonitorId = monitors[1].StableId, Opacity = 0 }
                    ]
                }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var manualDimmingApplied = await WaitUntilAsync(
            () =>
            {
                var primary = overlay.LastEffect(monitors[0].StableId);
                var secondary = overlay.LastEffect(monitors[1].StableId);
                return primary is not null &&
                    secondary is not null &&
                    primary.GlobalReason == EffectReason.Manual &&
                    Math.Abs(primary.GlobalOpacity - 0.25) < 0.001 &&
                    secondary.GlobalReason == EffectReason.None &&
                    Math.Abs(secondary.GlobalOpacity) < 0.001;
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
                UserMaximumOpacity = 0.25,
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                FlashGuard = new FlashDetectorOptions
                {
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
                var effect = overlay.LastEffect(monitors[0].StableId);
                return primary.FlashEvents > 0 &&
                    primary.RedFlashEvents == 0 &&
                    primary.ActiveReason == EffectReason.RepetitiveFlash &&
                    primary.RegionalMaskCount > 0 &&
                    effect is not null &&
                    effect.GlobalOpacity >= 0.17 &&
                    effect.RegionalMasks.Count > 0 &&
                    CombinedOpacity(effect.GlobalOpacity, effect.RegionalMasks.Max(mask => mask.Opacity)) >= 0.24 &&
                    secondary.FlashEvents == 0;
            },
            TimeSpan.FromSeconds(3));

        Assert.True(flashDetected);
    }

    [Fact]
    public async Task Start_AppliesSpikeGuardWithinResponsiveBudgetAfterDefaultWarmup()
    {
        var monitors = TestMonitors();
        var capture = new BrightnessSequenceCapture([32, 34, 32, 255]);
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            capture,
            overlay,
            AutomaticSpikeOnlySettings(),
            new MutableMonitorCatalog([monitors[0]]),
            analysisInterval: TimeSpan.FromMilliseconds(20));

        var startedAt = Stopwatch.GetTimestamp();
        controller.Start();

        var spikeDetected = await WaitUntilAsync(
            () => overlay.LastEffect(monitors[0].StableId)?.GlobalReason == EffectReason.BrightnessSpike,
            TimeSpan.FromSeconds(1));
        var elapsed = Stopwatch.GetElapsedTime(startedAt);

        Assert.True(spikeDetected, $"Spike guard did not apply within 1s; captures={capture.CaptureCount(monitors[0].StableId)}.");
        Assert.True(elapsed <= TimeSpan.FromMilliseconds(350), $"Spike guard took {elapsed.TotalMilliseconds:0} ms.");
    }

    [Fact]
    public async Task Start_AppliesLocalizedSpikeGuardAsRegionalMaskWithinResponsiveBudget()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        var capture = new LocalizedSpikeAfterWarmupCapture(monitors[0].StableId);
        using var controller = new LumaBrakeController(
            capture,
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions
                {
                    LocalizedSpikeDetectionEnabled = true
                },
                FlashGuard = new FlashDetectorOptions { Enabled = false },
                StaticShade = new StaticShadeOptions { Enabled = false },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false }
            },
            new MutableMonitorCatalog([monitors[0]]),
            analysisInterval: TimeSpan.FromMilliseconds(20));

        var startedAt = Stopwatch.GetTimestamp();
        controller.Start();

        PipelineSnapshot? detectedSnapshot = null;
        var spikeDetected = await WaitUntilAsync(
            () =>
            {
                var effect = overlay.LastEffect(monitors[0].StableId);
                var snapshot = controller.Snapshot().Monitors.Single();
                var detected = snapshot.SpikeEvents > 0 &&
                    snapshot.RegionalMaskCount > 0 &&
                    snapshot.RegionalMasks.Count > 0 &&
                    effect is not null &&
                    effect.GlobalReason == EffectReason.BrightnessSpike &&
                    effect.GlobalOpacity <= 0.001 &&
                    effect.RegionalMasks.Count > 0;
                if (detected)
                {
                    detectedSnapshot = controller.Snapshot();
                }

                return detected;
            },
            TimeSpan.FromSeconds(1));
        var elapsed = Stopwatch.GetElapsedTime(startedAt);

        Assert.True(spikeDetected);
        var finalSnapshot = Assert.IsType<PipelineSnapshot>(detectedSnapshot);
        var monitorSnapshot = finalSnapshot.Monitors.Single();
        var snapshotMask = Assert.Single(finalSnapshot.RegionalMasks);
        var monitorMask = Assert.Single(monitorSnapshot.RegionalMasks);
        Assert.Equal(EffectReason.BrightnessSpike, snapshotMask.Reason);
        Assert.Equal(snapshotMask, monitorMask);
        Assert.True(
            elapsed <= TimeSpan.FromMilliseconds(700),
            $"Localized spike guard took {elapsed.TotalMilliseconds:0} ms; captures={capture.CaptureCount(monitors[0].StableId)}.");
    }

    [Fact]
    public async Task Start_AppliesFullFlashGuardWithinResponsiveBudget()
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
                    Cooldown = TimeSpan.FromSeconds(5),
                    Hold = TimeSpan.FromSeconds(5),
                    Release = TimeSpan.FromSeconds(5)
                },
                StaticShade = new StaticShadeOptions { Enabled = false },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false }
            },
            new MutableMonitorCatalog([monitors[0]]),
            analysisInterval: TimeSpan.FromMilliseconds(20));

        var startedAt = Stopwatch.GetTimestamp();
        controller.Start();

        var flashDetected = await WaitUntilAsync(
            () => overlay.LastEffect(monitors[0].StableId)?.GlobalReason == EffectReason.RepetitiveFlash,
            TimeSpan.FromSeconds(1));
        var elapsed = Stopwatch.GetElapsedTime(startedAt);

        Assert.True(flashDetected);
        Assert.True(elapsed <= TimeSpan.FromMilliseconds(450), $"Flash guard took {elapsed.TotalMilliseconds:0} ms.");
    }

    [Fact]
    public async Task Start_AppliesLocalizedFlashGuardWithDefaultOptionsWithinResponsiveBudget()
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
                    Cooldown = TimeSpan.FromSeconds(5),
                    Hold = TimeSpan.FromSeconds(5),
                    Release = TimeSpan.FromSeconds(5)
                },
                StaticShade = new StaticShadeOptions { Enabled = false },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false }
            },
            new MutableMonitorCatalog([monitors[0]]),
            analysisInterval: TimeSpan.FromMilliseconds(20));

        var startedAt = Stopwatch.GetTimestamp();
        controller.Start();

        var flashDetected = await WaitUntilAsync(
            () =>
            {
                var effect = overlay.LastEffect(monitors[0].StableId);
                var snapshot = controller.Snapshot().Monitors.Single();
                return snapshot.FlashEvents > 0 &&
                    snapshot.RegionalMaskCount > 0 &&
                    effect is not null &&
                    effect.GlobalReason == EffectReason.RepetitiveFlash &&
                    effect.RegionalMasks.Count > 0;
            },
            TimeSpan.FromSeconds(1));
        var elapsed = Stopwatch.GetElapsedTime(startedAt);

        Assert.True(flashDetected);
        Assert.True(elapsed <= TimeSpan.FromMilliseconds(350), $"Localized flash guard took {elapsed.TotalMilliseconds:0} ms.");
    }

    [Fact]
    public async Task Start_CountsRedDominantFlashEventsInSnapshots()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RedFlashingCapture(monitors[0].StableId),
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

        var redFlashDetected = await WaitUntilAsync(
            () =>
            {
                var snapshot = controller.Snapshot();
                var monitor = snapshot.Monitors.Single();
                return snapshot.FlashEvents > 0 &&
                    snapshot.RedFlashEvents > 0 &&
                    monitor.RedFlashEvents > 0 &&
                    monitor.ActiveReason == EffectReason.RepetitiveFlash &&
                    overlay.LastEffect(monitors[0].StableId)?.GlobalOpacity > 0;
            },
            TimeSpan.FromSeconds(3));

        Assert.True(redFlashDetected);
    }

    [Fact]
    public async Task Start_AddsGlobalSafetyFloorAndRegionalMaskForLocalizedRedFlash()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new LocalizedRedFlashingCapture(monitors[0].StableId),
            overlay,
            new LumaBrakeSettings
            {
                UserMaximumOpacity = 0.25,
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                FlashGuard = new FlashDetectorOptions
                {
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
                var effect = overlay.LastEffect(monitors[0].StableId);
                return primary.FlashEvents > 0 &&
                    primary.RedFlashEvents > 0 &&
                    primary.ActiveReason == EffectReason.RepetitiveFlash &&
                    primary.RegionalMaskCount > 0 &&
                    primary.RegionalMaskMaxOpacity > 0 &&
                    effect is not null &&
                    effect.GlobalOpacity >= 0.17 &&
                    effect.RegionalMasks.Count > 0 &&
                    CombinedOpacity(effect.GlobalOpacity, effect.RegionalMasks.Max(mask => mask.Opacity)) >= 0.24 &&
                    secondary.FlashEvents == 0 &&
                    secondary.RedFlashEvents == 0;
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
                    snapshot.RedFlashEvents == 0 &&
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
    public async Task Start_ClearsStaticShadeRegionalMaskImmediatelyAfterSceneChange()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        var capture = new MutableLocalizedStaticCapture(monitors[0].StableId);
        using var controller = new LumaBrakeController(
            capture,
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
                    Hold = TimeSpan.FromSeconds(5),
                    Release = TimeSpan.FromSeconds(5)
                },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var staticShadeDetected = await WaitUntilAsync(
            () => overlay.LastEffect(monitors[0].StableId)?.RegionalMasks.Any(mask => mask.Reason == EffectReason.StaticShade) == true,
            TimeSpan.FromSeconds(3));

        Assert.True(staticShadeDetected);

        capture.IsBright = false;
        var staticShadeCleared = await WaitUntilAsync(
            () =>
            {
                var effect = overlay.LastEffect(monitors[0].StableId);
                return effect is not null &&
                    !effect.RegionalMasks.Any(mask => mask.Reason == EffectReason.StaticShade);
            },
            TimeSpan.FromSeconds(1));

        Assert.True(staticShadeCleared);
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
    public async Task Start_AppliesAdaptiveDimmingWithDefaultOptionsWithinComfortBudget()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                FlashGuard = new FlashDetectorOptions { Enabled = false },
                StaticShade = new StaticShadeOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false }
            },
            new MutableMonitorCatalog([monitors[0]]),
            analysisInterval: TimeSpan.FromMilliseconds(20));

        var startedAt = Stopwatch.GetTimestamp();
        controller.Start();

        var adaptiveDetected = await WaitUntilAsync(
            () => overlay.LastEffect(monitors[0].StableId)?.GlobalReason == EffectReason.AdaptiveContent,
            TimeSpan.FromSeconds(2));
        var elapsed = Stopwatch.GetElapsedTime(startedAt);

        Assert.True(adaptiveDetected);
        Assert.True(elapsed <= TimeSpan.FromMilliseconds(900), $"Adaptive dimming took {elapsed.TotalMilliseconds:0} ms.");
    }

    [Fact]
    public async Task Start_SuppressesAutomaticEffectsForMatchingApplicationRule()
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
                },
                ApplicationRules = new ApplicationRuleOptions
                {
                    Entries =
                    [
                        new ApplicationRule
                        {
                            Id = "video",
                            ProcessName = "video-player"
                        }
                    ]
                }
            },
            new MutableMonitorCatalog(monitors),
            foregroundApplicationProvider: new FixedForegroundApplicationProvider(
                new ForegroundApplicationInfo("video-player.exe", "Playback")));

        controller.Start();

        var framesAnalyzed = await WaitUntilAsync(
            () => controller.Snapshot().FramesAnalyzed >= monitors.Length * 3,
            TimeSpan.FromSeconds(2));

        Assert.True(framesAnalyzed);
        Assert.All(
            controller.Snapshot().Monitors,
            monitor =>
            {
                Assert.Equal(EffectReason.None, monitor.ActiveReason);
                Assert.Equal(0, monitor.OverlayOpacity);
                Assert.Equal(0, overlay.LastEffect(monitor.StableId)?.GlobalOpacity ?? 0);
            });
    }

    [Fact]
    public async Task Start_KeepsManualDimmingWhenApplicationRuleSuppressesAutomaticEffects()
    {
        var monitors = TestMonitors();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            new RecordingOverlay(),
            new LumaBrakeSettings
            {
                ManualDimmingOpacity = 0.25,
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                ApplicationRules = new ApplicationRuleOptions
                {
                    Entries =
                    [
                        new ApplicationRule
                        {
                            Id = "video",
                            ProcessName = "video-player"
                        }
                    ]
                }
            },
            new MutableMonitorCatalog(monitors),
            foregroundApplicationProvider: new FixedForegroundApplicationProvider(
                new ForegroundApplicationInfo("video-player", "Playback")));

        controller.Start();

        var manualApplied = await WaitUntilAsync(
            () => controller.Snapshot().Monitors.All(monitor =>
                monitor.ActiveReason == EffectReason.Manual &&
                Math.Abs(monitor.OverlayOpacity - 0.25) < 0.001),
            TimeSpan.FromSeconds(2));

        Assert.True(manualApplied);
    }

    [Fact]
    public async Task Start_SuppressesAutomaticEffectsOnAdvancedColorMonitors()
    {
        var monitors = TestMonitors(primaryAdvancedColorEnabled: true);
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
                },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false }
            },
            new MutableMonitorCatalog([monitors[0]]));

        controller.Start();

        var automaticEffectsSuppressed = await WaitUntilAsync(
            () =>
            {
                var monitor = controller.Snapshot().Monitors.Single();
                var effect = overlay.LastEffect(monitor.StableId);
                return monitor.FramesAnalyzed >= 3 &&
                    monitor.AdvancedColor.RequiresConservativeHandling &&
                    monitor.ActiveReason == EffectReason.None &&
                    monitor.SpikeEvents == 0 &&
                    monitor.FlashEvents == 0 &&
                    Math.Abs(monitor.OverlayOpacity) < 0.001 &&
                    effect is not null &&
                    Math.Abs(effect.GlobalOpacity) < 0.001;
            },
            TimeSpan.FromSeconds(3));

        Assert.True(automaticEffectsSuppressed);
    }

    [Fact]
    public async Task Start_KeepsManualDimmingOnAdvancedColorMonitors()
    {
        var monitors = TestMonitors(primaryAdvancedColorEnabled: true);
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            new RecordingOverlay(),
            new LumaBrakeSettings
            {
                ManualDimmingOpacity = 0.25,
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false }
            },
            new MutableMonitorCatalog([monitors[0]]));

        controller.Start();

        var manualApplied = await WaitUntilAsync(
            () =>
            {
                var monitor = controller.Snapshot().Monitors.Single();
                return monitor.AdvancedColor.RequiresConservativeHandling &&
                    monitor.ActiveReason == EffectReason.Manual &&
                    Math.Abs(monitor.OverlayOpacity - 0.25) < 0.001;
            },
            TimeSpan.FromSeconds(2));

        Assert.True(manualApplied);
    }

    [Fact]
    public async Task Start_SkipsNearEmptyCaptureFramesBeforeSpikeDetectorState()
    {
        var monitors = TestMonitors();
        var capture = new NearEmptyThenBrightCapture(monitors[0].StableId, nearEmptyFrames: 3);
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            capture,
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions
                {
                    WarmupSamples = 1,
                    MinimumActivationLuminance = 0.10,
                    AbsoluteIncreaseThreshold = 0.05,
                    RelativeIncreaseThreshold = 1.10,
                    Cooldown = TimeSpan.Zero,
                    Hold = TimeSpan.FromSeconds(5),
                    Release = TimeSpan.FromSeconds(5)
                },
                FlashGuard = new FlashDetectorOptions { Enabled = false },
                StaticShade = new StaticShadeOptions { Enabled = false },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false }
            },
            new MutableMonitorCatalog([monitors[0]]));

        controller.Start();

        var brightFramesAnalyzed = await WaitUntilAsync(
            () => capture.CaptureCount(monitors[0].StableId) >= 6,
            TimeSpan.FromSeconds(2));
        Assert.True(brightFramesAnalyzed);

        var monitor = controller.Snapshot().Monitors.Single();
        Assert.True(monitor.CaptureAnomalyEvents >= 3);
        Assert.NotNull(monitor.LastCaptureAnomalyAt);
        Assert.False(monitor.IsCaptureAnomalyActive);
        Assert.Equal(0, monitor.SpikeEvents);
        Assert.Equal(EffectReason.None, monitor.ActiveReason);
        Assert.Equal(0, overlay.LastEffect(monitors[0].StableId)?.GlobalOpacity ?? 0);
    }

    [Fact]
    public async Task Start_KeepsManualDimmingDuringNearEmptyCaptureFrames()
    {
        var monitors = TestMonitors();
        using var controller = new LumaBrakeController(
            new NearEmptyCapture(),
            new RecordingOverlay(),
            new LumaBrakeSettings
            {
                ManualDimmingOpacity = 0.25,
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false }
            },
            new MutableMonitorCatalog([monitors[0]]));

        controller.Start();

        var manualApplied = await WaitUntilAsync(
            () =>
            {
                var snapshot = controller.Snapshot();
                var monitor = snapshot.Monitors.Single();
                return snapshot.HasActiveCaptureAnomaly &&
                    monitor.IsCaptureAnomalyActive &&
                    monitor.CaptureAnomalyEvents >= 3 &&
                    monitor.ActiveReason == EffectReason.Manual &&
                    Math.Abs(monitor.OverlayOpacity - 0.25) < 0.001;
            },
            TimeSpan.FromSeconds(2));

        Assert.True(manualApplied);
    }

    [Fact]
    public async Task Start_KeepsFocusDimmingDuringNearEmptyCaptureFrames()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new NearEmptyCapture(),
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                FlashGuard = new FlashDetectorOptions { Enabled = false },
                StaticShade = new StaticShadeOptions { Enabled = false },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false },
                FocusDimming = new FocusDimmingOptions
                {
                    Enabled = true,
                    Opacity = 0.22,
                    Hold = TimeSpan.FromMilliseconds(250),
                    Release = TimeSpan.FromMilliseconds(250)
                }
            },
            new MutableMonitorCatalog(monitors),
            new FixedFocusedMonitorProvider(monitors[0].StableId));

        controller.Start();

        var focusApplied = await WaitUntilAsync(
            () =>
            {
                var snapshot = controller.Snapshot();
                var primary = snapshot.Monitors.Single(monitor => monitor.StableId == monitors[0].StableId);
                var secondary = snapshot.Monitors.Single(monitor => monitor.StableId == monitors[1].StableId);
                return snapshot.HasActiveCaptureAnomaly &&
                    primary.ActiveReason == EffectReason.None &&
                    secondary.IsCaptureAnomalyActive &&
                    secondary.ActiveReason == EffectReason.FocusDimming &&
                    Math.Abs(secondary.OverlayOpacity - 0.22) < 0.001 &&
                    overlay.LastEffect(monitors[1].StableId)?.GlobalReason == EffectReason.FocusDimming;
            },
            TimeSpan.FromSeconds(2));

        Assert.True(focusApplied);
    }

    [Fact]
    public async Task ApplySettings_RebuildsDetectorsAndClearsActiveEffects()
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
                },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var adaptiveDetected = await WaitUntilAsync(
            () => controller.Snapshot().Monitors.All(monitor => monitor.ActiveReason == EffectReason.AdaptiveContent),
            TimeSpan.FromSeconds(3));
        Assert.True(adaptiveDetected);

        controller.ApplySettings(
            controller.Settings with { AdaptiveContent = controller.Settings.AdaptiveContent with { Enabled = false } },
            resetDetectorState: true);

        var effectsCleared = await WaitUntilAsync(
            () => controller.Snapshot().Monitors.All(monitor =>
                monitor.ActiveReason == EffectReason.None &&
                Math.Abs(monitor.OverlayOpacity) < 0.001),
            TimeSpan.FromSeconds(2));

        Assert.True(effectsCleared);
        Assert.True(overlay.HideAllCalls > 0);
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
    public async Task Start_ReleasesInactiveDimmingWhenFocusedMonitorMovesToSecondary()
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
                FlashGuard = new FlashDetectorOptions { Enabled = false },
                StaticShade = new StaticShadeOptions { Enabled = false },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                FocusDimming = new FocusDimmingOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions
                {
                    MinimumInactiveDuration = TimeSpan.FromMilliseconds(100),
                    MaximumFrameGap = TimeSpan.FromMilliseconds(500),
                    Hold = TimeSpan.FromSeconds(30),
                    Release = TimeSpan.FromSeconds(30),
                    Opacity = 0.22
                }
            },
            new MutableMonitorCatalog(monitors),
            focusedMonitorProvider);

        controller.Start();

        var secondaryDimmed = await WaitUntilAsync(
            () => controller.Snapshot().Monitors.Single(monitor => monitor.StableId == monitors[1].StableId).ActiveReason == EffectReason.InactiveMonitor,
            TimeSpan.FromSeconds(3));
        Assert.True(secondaryDimmed);

        focusedMonitorProvider.FocusedStableId = monitors[1].StableId;

        var released = await WaitUntilAsync(
            () =>
            {
                var secondary = controller.Snapshot().Monitors.Single(monitor => monitor.StableId == monitors[1].StableId);
                var effect = overlay.LastEffect(monitors[1].StableId);
                return secondary.ActiveReason == EffectReason.None &&
                    secondary.OverlayOpacity == 0 &&
                    effect?.GlobalReason != EffectReason.InactiveMonitor &&
                    effect?.GlobalOpacity == 0;
            },
            TimeSpan.FromSeconds(2));

        Assert.True(released);
    }

    [Fact]
    public async Task Start_ReleasesInactiveDimmingWhenCursorMovesToSecondary()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        var cursorMonitorProvider = new MutableCursorMonitorProvider(monitors[0].StableId);
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                FlashGuard = new FlashDetectorOptions { Enabled = false },
                StaticShade = new StaticShadeOptions { Enabled = false },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                FocusDimming = new FocusDimmingOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions
                {
                    MinimumInactiveDuration = TimeSpan.FromMilliseconds(100),
                    MaximumFrameGap = TimeSpan.FromMilliseconds(500),
                    Hold = TimeSpan.FromSeconds(30),
                    Release = TimeSpan.FromSeconds(30),
                    Opacity = 0.22
                }
            },
            new MutableMonitorCatalog(monitors),
            cursorMonitorProvider: cursorMonitorProvider);

        controller.Start();

        var secondaryDimmed = await WaitUntilAsync(
            () => controller.Snapshot().Monitors.Single(monitor => monitor.StableId == monitors[1].StableId).ActiveReason == EffectReason.InactiveMonitor,
            TimeSpan.FromSeconds(3));
        Assert.True(secondaryDimmed);

        cursorMonitorProvider.CursorStableId = monitors[1].StableId;

        var released = await WaitUntilAsync(
            () =>
            {
                var secondary = controller.Snapshot().Monitors.Single(monitor => monitor.StableId == monitors[1].StableId);
                var effect = overlay.LastEffect(monitors[1].StableId);
                return secondary.ActiveReason == EffectReason.None &&
                    secondary.OverlayOpacity == 0 &&
                    effect?.GlobalReason != EffectReason.InactiveMonitor &&
                    effect?.GlobalOpacity == 0;
            },
            TimeSpan.FromSeconds(2));

        Assert.True(released);
    }

    [Fact]
    public async Task Start_ContinuesInactiveDimmingWhenCursorMonitorProviderFails()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                FlashGuard = new FlashDetectorOptions { Enabled = false },
                StaticShade = new StaticShadeOptions { Enabled = false },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                FocusDimming = new FocusDimmingOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions
                {
                    MinimumInactiveDuration = TimeSpan.FromMilliseconds(100),
                    MaximumFrameGap = TimeSpan.FromMilliseconds(500),
                    Hold = TimeSpan.FromMilliseconds(250),
                    Release = TimeSpan.FromMilliseconds(250),
                    Opacity = 0.22
                }
            },
            new MutableMonitorCatalog(monitors),
            cursorMonitorProvider: ThrowingCursorMonitorProvider.Instance);

        controller.Start();

        var inactiveDetected = await WaitUntilAsync(
            () =>
            {
                var secondary = controller.Snapshot().Monitors.Single(monitor => monitor.StableId == monitors[1].StableId);
                return secondary.ActiveReason == EffectReason.InactiveMonitor &&
                    secondary.OverlayOpacity > 0 &&
                    overlay.LastEffect(monitors[1].StableId)?.GlobalReason == EffectReason.InactiveMonitor;
            },
            TimeSpan.FromSeconds(3));

        Assert.True(inactiveDetected);
    }

    [Fact]
    public async Task Start_DoesNotInactiveDimSecondaryMonitorWhenTilesMoveWithStableGlobalMetrics()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new MovingTileActivityCapture(monitors[1].StableId),
            overlay,
            new LumaBrakeSettings
            {
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                FlashGuard = new FlashDetectorOptions { Enabled = false },
                StaticShade = new StaticShadeOptions { Enabled = false },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions
                {
                    MinimumInactiveDuration = TimeSpan.FromMilliseconds(100),
                    MaximumFrameGap = TimeSpan.FromMilliseconds(500),
                    MaximumAverageDelta = 0.02,
                    MaximumPercentile95Delta = 0.02,
                    MaximumChangedTileRatio = 0.08,
                    TileChangeAverageDelta = 0.08,
                    TileChangePercentile95Delta = 0.10,
                    Hold = TimeSpan.FromMilliseconds(250),
                    Release = TimeSpan.FromMilliseconds(250),
                    Opacity = 0.22
                }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var framesObserved = await WaitUntilAsync(
            () => controller.Snapshot().FramesAnalyzed >= monitors.Length * 8,
            TimeSpan.FromSeconds(3));

        Assert.True(framesObserved);
        var secondary = controller.Snapshot().Monitors.Single(monitor => monitor.StableId == monitors[1].StableId);
        Assert.NotEqual(EffectReason.InactiveMonitor, secondary.ActiveReason);
        Assert.NotEqual(EffectReason.InactiveMonitor, overlay.LastEffect(monitors[1].StableId)?.GlobalReason);
        Assert.Equal(0, secondary.OverlayOpacity, precision: 12);
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
    public async Task RefreshMonitors_DoesNotClearFocusDimmingWhenOnlyLastSeenChanges()
    {
        var monitors = TestMonitors();
        var refreshedMonitors = monitors
            .Select(monitor => monitor with { LastSeenAt = DateTimeOffset.UtcNow.AddMinutes(1) })
            .ToArray();
        var catalog = new MutableMonitorCatalog(monitors);
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
            catalog,
            new FixedFocusedMonitorProvider(monitors[0].StableId));

        controller.Start();

        var focusDimmingApplied = await WaitUntilAsync(
            () => controller.Snapshot().Monitors.Single(monitor => monitor.StableId == monitors[1].StableId).ActiveReason == EffectReason.FocusDimming,
            TimeSpan.FromSeconds(2));
        Assert.True(focusDimmingApplied);
        var beforeRefresh = controller.Snapshot().Monitors.Single(monitor => monitor.StableId == monitors[1].StableId);
        Assert.True(beforeRefresh.FramesAnalyzed > 0);
        Assert.True(beforeRefresh.AverageLuminance > 0.50);
        Assert.True(beforeRefresh.Percentile95Luminance > 0.50);

        overlay.ClearEffectHistory();
        catalog.Monitors = refreshedMonitors;
        controller.RefreshMonitors();

        var secondary = controller.Snapshot().Monitors.Single(monitor => monitor.StableId == monitors[1].StableId);
        Assert.True(secondary.FramesAnalyzed >= beforeRefresh.FramesAnalyzed);
        Assert.True(secondary.AverageLuminance > 0.50);
        Assert.True(secondary.Percentile95Luminance > 0.50);
        Assert.Equal(EffectReason.FocusDimming, secondary.ActiveReason);
        Assert.True(secondary.OverlayOpacity > 0);
        Assert.DoesNotContain(
            overlay.EffectsFor(monitors[1].StableId),
            effect => effect.GlobalOpacity <= 0.001 && effect.GlobalReason == EffectReason.None);
    }

    [Fact]
    public async Task Start_KeepsFocusDimmingStableDuringBriefUnknownFocusLookup()
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
                    Hold = TimeSpan.FromMilliseconds(20),
                    Release = TimeSpan.FromMilliseconds(40)
                }
            },
            new MutableMonitorCatalog(monitors),
            new FlakyFocusedMonitorProvider(monitors[0].StableId));

        controller.Start();

        var focusDimmingApplied = await WaitUntilAsync(
            () => controller.Snapshot().Monitors.Single(monitor => monitor.StableId == monitors[1].StableId).ActiveReason == EffectReason.FocusDimming,
            TimeSpan.FromSeconds(2));
        Assert.True(focusDimmingApplied);

        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        var secondary = controller.Snapshot().Monitors.Single(monitor => monitor.StableId == monitors[1].StableId);
        Assert.Equal(EffectReason.FocusDimming, secondary.ActiveReason);
        Assert.InRange(secondary.OverlayOpacity, 0.16, 0.18);
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
    public async Task Start_AppliesNightColorAsRuntimeEffect()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            NightColorOnlySettings(),
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var nightColorApplied = await WaitUntilAsync(
            () => monitors.All(monitor =>
            {
                var effect = overlay.LastEffect(monitor.StableId);
                return effect is not null &&
                    effect.GlobalReason == EffectReason.NightColor &&
                    Math.Abs(effect.GlobalOpacity - 0.25) < 0.001 &&
                    effect.Tint.Enabled &&
                    effect.Tint.Opacity > 0;
            }),
            TimeSpan.FromSeconds(2));

        Assert.True(nightColorApplied);
    }

    [Fact]
    public async Task Start_UsesDisplayNightColorWithoutOverlayTintWhenAvailable()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        var displayController = new RecordingNightColorDisplayController(applyResult: true);
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            NightColorOnlySettings(),
            new MutableMonitorCatalog(monitors),
            nightColorDisplayController: displayController);

        controller.Start();

        var displayNightColorApplied = await WaitUntilAsync(
            () => displayController.LastState?.TemperatureKelvin == 3400 &&
                monitors.All(monitor => overlay.LastEffect(monitor.StableId) is not null),
            TimeSpan.FromSeconds(2));

        Assert.True(displayNightColorApplied);
        Assert.True(displayController.LastState?.Enabled);
        Assert.Equal(3400, displayController.LastState?.TemperatureKelvin);
        Assert.All(monitors, monitor =>
        {
            var effect = overlay.LastEffect(monitor.StableId);
            Assert.NotNull(effect);
            Assert.Equal(EffectReason.None, effect.GlobalReason);
            Assert.Equal(0, effect.GlobalOpacity, precision: 12);
            Assert.False(effect.Tint.Enabled);
        });
    }

    [Fact]
    public async Task ApplySettings_DisablingNightColorClearsDisplayControllerImmediately()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        var displayController = new RecordingNightColorDisplayController(applyResult: true);
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            NightColorOnlySettings(),
            new MutableMonitorCatalog(monitors),
            nightColorDisplayController: displayController,
            analysisInterval: TimeSpan.FromMilliseconds(20));

        controller.Start();

        var displayNightColorApplied = await WaitUntilAsync(
            () => displayController.LastState?.Enabled == true,
            TimeSpan.FromSeconds(2));
        Assert.True(displayNightColorApplied);

        controller.ApplySettings(controller.Settings with
        {
            NightColor = controller.Settings.NightColor.Clamp() with { Enabled = false }
        });

        Assert.True(displayController.ClearCalls > 0);
    }

    [Fact]
    public async Task Start_AppliesNightColorOverlayWhenRuntimeProtectionIsDisabled()
    {
        var monitors = TestMonitors();
        var capture = new RecordingCapture();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            capture,
            overlay,
            NightColorOnlySettings() with { Enabled = false },
            new MutableMonitorCatalog(monitors),
            analysisInterval: TimeSpan.FromMilliseconds(20));

        controller.Start();

        var nightColorApplied = await WaitUntilAsync(
            () => monitors.All(monitor =>
            {
                var effect = overlay.LastEffect(monitor.StableId);
                return effect is not null &&
                    effect.GlobalReason == EffectReason.NightColor &&
                    Math.Abs(effect.GlobalOpacity - 0.25) < 0.001 &&
                    effect.Tint.Enabled &&
                    effect.Tint.Opacity > 0;
            }),
            TimeSpan.FromSeconds(2));

        Assert.True(nightColorApplied);
        Assert.False(controller.Settings.Enabled);
        Assert.All(monitors, monitor => Assert.Equal(0, capture.CaptureCount(monitor.StableId)));
    }

    [Fact]
    public async Task Start_UsesDisplayNightColorWhenRuntimeProtectionIsDisabled()
    {
        var monitors = TestMonitors();
        var capture = new RecordingCapture();
        var overlay = new RecordingOverlay();
        var displayController = new RecordingNightColorDisplayController(applyResult: true);
        using var controller = new LumaBrakeController(
            capture,
            overlay,
            NightColorOnlySettings() with { Enabled = false },
            new MutableMonitorCatalog(monitors),
            nightColorDisplayController: displayController,
            analysisInterval: TimeSpan.FromMilliseconds(20));

        controller.Start();

        var displayNightColorApplied = await WaitUntilAsync(
            () => displayController.LastState?.TemperatureKelvin == 3400 &&
                monitors.All(monitor => overlay.LastEffect(monitor.StableId) is not null),
            TimeSpan.FromSeconds(2));

        Assert.True(displayNightColorApplied);
        Assert.True(displayController.LastState?.Enabled);
        Assert.Equal(3400, displayController.LastState?.TemperatureKelvin);
        Assert.False(controller.Settings.Enabled);
        Assert.All(monitors, monitor =>
        {
            Assert.Equal(0, capture.CaptureCount(monitor.StableId));
            var effect = overlay.LastEffect(monitor.StableId);
            Assert.NotNull(effect);
            Assert.Equal(EffectReason.None, effect.GlobalReason);
            Assert.Equal(0, effect.GlobalOpacity, precision: 12);
            Assert.False(effect.Tint.Enabled);
        });
    }

    [Fact]
    public void PreviewNightColor_AppliesDisplayStateImmediatelyAndThrottlesRapidFrames()
    {
        var monitors = TestMonitors();
        var displayController = new RecordingNightColorDisplayController(applyResult: true);
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            new RecordingOverlay(),
            NightColorOnlySettings(),
            new MutableMonitorCatalog(monitors),
            nightColorDisplayController: displayController);

        var previewState = new NightColorState(
            true,
            2800,
            0.72,
            1,
            NightColorEngine.TintForTemperature(2800, 1),
            0.28,
            "preview");

        var applied = controller.PreviewNightColor(previewState);

        Assert.True(applied);
        Assert.Equal(1, displayController.ApplyCalls);
        Assert.NotNull(displayController.LastState);
        Assert.Equal(6380, displayController.LastState.TemperatureKelvin);
        Assert.Equal(0.986, displayController.LastState.Brightness, precision: 12);

        controller.PreviewNightColor(previewState);

        Assert.NotNull(displayController.LastState);
        Assert.Equal(1, displayController.ApplyCalls);
        Assert.Equal(6380, displayController.LastState.TemperatureKelvin);
        Assert.Equal(0.986, displayController.LastState.Brightness, precision: 12);
    }

    [Fact]
    public async Task Start_SuspendsContentAnalysisWhileNightColorPreviewRuns()
    {
        var monitors = TestMonitors();
        var capture = new RecordingCapture();
        var displayController = new RecordingNightColorDisplayController(applyResult: true);
        using var controller = new LumaBrakeController(
            capture,
            new RecordingOverlay(),
            new LumaBrakeSettings(),
            new MutableMonitorCatalog(monitors),
            nightColorDisplayController: displayController,
            analysisInterval: TimeSpan.FromMilliseconds(20));

        controller.PreviewNightColor(new NightColorState(
            true,
            2800,
            0.72,
            1,
            NightColorEngine.TintForTemperature(2800, 1),
            0.28,
            "preview"));
        controller.Start();

        await Task.Delay(TimeSpan.FromMilliseconds(180), TestContext.Current.CancellationToken);

        Assert.Equal(0, controller.Snapshot().FramesAnalyzed);
        Assert.All(monitors, monitor => Assert.Equal(0, capture.CaptureCount(monitor.StableId)));

        controller.StopNightColorPreview();

        var resumed = await WaitUntilAsync(
            () => controller.Snapshot().FramesAnalyzed > 0,
            TimeSpan.FromSeconds(2));

        Assert.True(resumed);
    }

    [Fact]
    public void PreviewNightColor_FallsBackToOverlayWhenDisplayCannotApply()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        var displayController = new RecordingNightColorDisplayController(applyResult: false);
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            NightColorOnlySettings(),
            new MutableMonitorCatalog(monitors),
            nightColorDisplayController: displayController);

        var applied = controller.PreviewNightColor(new NightColorState(
            true,
            2800,
            0.72,
            1,
            NightColorEngine.TintForTemperature(2800, 1),
            0.28,
            "preview"));

        Assert.True(applied);
        Assert.Equal(1, displayController.ApplyCalls);
        Assert.All(monitors, monitor =>
        {
            var effect = overlay.LastEffect(monitor.StableId);
            Assert.NotNull(effect);
            Assert.Equal(EffectReason.NightColor, effect.GlobalReason);
            Assert.Equal(0.02, effect.GlobalOpacity, precision: 12);
            Assert.True(effect.Tint.Enabled);
        });
    }

    [Fact]
    public void StopNightColorPreview_RestoresConfiguredDisplayState()
    {
        var monitors = TestMonitors();
        var displayController = new RecordingNightColorDisplayController(applyResult: true);
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            new RecordingOverlay(),
            NightColorOnlySettings() with
            {
                NightColor = NightColorOnlySettings().NightColor with { Enabled = false }
            },
            new MutableMonitorCatalog(monitors),
            nightColorDisplayController: displayController);

        controller.PreviewNightColor(new NightColorState(
            true,
            2800,
            0.72,
            1,
            NightColorEngine.TintForTemperature(2800, 1),
            0.28,
            "preview"));

        controller.StopNightColorPreview();

        Assert.True(displayController.ApplyCalls >= 2);
        Assert.NotNull(displayController.LastState);
        Assert.False(displayController.LastState.Enabled);
        Assert.Equal("disabled", displayController.LastState.Reason);
    }

    [Fact]
    public async Task EmergencyDisable_DisablesNightColorSoItDoesNotReturnWhileRuntimeIsOff()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        var displayController = new RecordingNightColorDisplayController(applyResult: false);
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            NightColorOnlySettings(),
            new MutableMonitorCatalog(monitors),
            nightColorDisplayController: displayController,
            analysisInterval: TimeSpan.FromMilliseconds(20));

        controller.Start();

        var nightColorApplied = await WaitUntilAsync(
            () => controller.Snapshot().Monitors.All(monitor => monitor.ActiveReason == EffectReason.NightColor),
            TimeSpan.FromSeconds(2));
        Assert.True(nightColorApplied);

        controller.EmergencyDisable();
        await Task.Delay(TimeSpan.FromMilliseconds(160), TestContext.Current.CancellationToken);

        Assert.False(controller.Settings.Enabled);
        Assert.False(controller.Settings.NightColor.Enabled);
        Assert.True(displayController.ClearCalls > 0);
        Assert.All(controller.Snapshot().Monitors, monitor =>
        {
            Assert.Equal(EffectReason.None, monitor.ActiveReason);
            Assert.Equal(0, monitor.OverlayOpacity);
        });
    }

    [Fact]
    public async Task Start_PausesNightColorForFullScreenForegroundApplication()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        var settings = NightColorOnlySettings();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            settings with
            {
                NightColor = settings.NightColor with { PauseInFullScreenApplications = true }
            },
            new MutableMonitorCatalog(monitors),
            foregroundApplicationProvider: new FixedForegroundApplicationProvider(
                new ForegroundApplicationInfo("video-player", "Playback", IsFullScreen: true)));

        controller.Start();

        var framesAnalyzed = await WaitUntilAsync(
            () => controller.Snapshot().FramesAnalyzed >= monitors.Length * 3,
            TimeSpan.FromSeconds(2));

        Assert.True(framesAnalyzed);
        Assert.All(
            controller.Snapshot().Monitors,
            monitor =>
            {
                Assert.Equal(EffectReason.None, monitor.ActiveReason);
                Assert.Equal(0, monitor.OverlayOpacity);
            });
        Assert.All(monitors, monitor =>
        {
            var effect = overlay.LastEffect(monitor.StableId);
            Assert.True(effect is null || effect.GlobalOpacity <= 0.001);
            Assert.True(effect is null || !effect.Tint.Enabled);
        });
    }

    [Fact]
    public async Task Start_KeepsNightColorWhenOneMonitorCaptureKeepsFailing()
    {
        var monitors = TestMonitors();
        var capture = new FailingMonitorCapture(monitors[0].StableId);
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            capture,
            overlay,
            NightColorOnlySettings(),
            new MutableMonitorCatalog(monitors),
            analysisInterval: TimeSpan.FromMilliseconds(20));

        controller.Start();

        var nightColorApplied = await WaitUntilAsync(
            () =>
            {
                var snapshot = controller.Snapshot();
                var failed = snapshot.Monitors.Single(monitor => monitor.StableId == monitors[0].StableId);
                var healthy = snapshot.Monitors.Single(monitor => monitor.StableId == monitors[1].StableId);
                var failedEffect = overlay.LastEffect(monitors[0].StableId);
                return snapshot.CaptureFailureEvents > 0 &&
                    failed.IsCaptureFailureActive &&
                    failed.ActiveReason == EffectReason.NightColor &&
                    Math.Abs(failed.OverlayOpacity - 0.25) < 0.001 &&
                    healthy.ActiveReason == EffectReason.NightColor &&
                    failedEffect is not null &&
                    failedEffect.Tint.Enabled &&
                    !overlay.EffectsFor(monitors[0].StableId).Any(effect =>
                        effect.GlobalReason == EffectReason.None &&
                        effect.GlobalOpacity <= 0.001);
            },
            TimeSpan.FromSeconds(3));

        Assert.True(nightColorApplied);
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
    public async Task Start_DoesNotDoubleApplyScheduleWhenMonitorManualDimmingAlreadyMatchesTarget()
    {
        var monitors = TestMonitors();
        var overlay = new RecordingOverlay();
        using var controller = new LumaBrakeController(
            new RecordingCapture(),
            overlay,
            new LumaBrakeSettings
            {
                ManualDimmingOpacity = 0.05,
                SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
                AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
                InactiveMonitor = new InactiveMonitorOptions { Enabled = false },
                MonitorManualDimming = new MonitorManualDimmingOptions
                {
                    Entries = [new MonitorManualDimmingOverride { MonitorId = monitors[0].StableId, Opacity = 0.18 }]
                },
                Schedules = new ScheduleOptions
                {
                    Enabled = true,
                    Entries = [ActiveSchedule(0.18, new TintOptions { Enabled = false })]
                }
            },
            new MutableMonitorCatalog(monitors));

        controller.Start();

        var opacityStable = await WaitUntilAsync(
            () =>
            {
                var primary = overlay.LastEffect(monitors[0].StableId);
                var secondary = overlay.LastEffect(monitors[1].StableId);
                return primary is not null &&
                    secondary is not null &&
                    Math.Abs(primary.GlobalOpacity - 0.18) < 0.001 &&
                    Math.Abs(secondary.GlobalOpacity - 0.18) < 0.001;
            },
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
        await Task.Delay(TimeSpan.FromMilliseconds(180), TestContext.Current.CancellationToken);

        Assert.False(controller.Settings.Enabled);
        Assert.All(controller.Snapshot().Monitors, monitor => Assert.Equal(0, monitor.OverlayOpacity));
        Assert.Equal(0, controller.Settings.ManualDimmingOpacity, precision: 12);
    }

    private static MonitorDescriptor[] TestMonitors(bool primaryAdvancedColorEnabled = false)
    {
        var primary = new MonitorDescriptor(
            "display-1",
            @"\\.\DISPLAY1",
            "Primary",
            IsPrimary: true,
            IsActive: true,
            new MonitorBounds(0, 0, 160, 90),
            new MonitorBounds(0, 0, 160, 90));

        if (primaryAdvancedColorEnabled)
        {
            primary = primary with { AdvancedColor = ActiveAdvancedColorState() };
        }

        var secondary = new MonitorDescriptor(
            "display-2",
            @"\\.\DISPLAY2",
            "Secondary",
            IsPrimary: false,
            IsActive: true,
            new MonitorBounds(160, 0, 160, 90),
            new MonitorBounds(160, 0, 160, 90));

        return [primary, secondary];
    }

    private static MonitorAdvancedColorState ActiveAdvancedColorState() => new(
        IsKnown: true,
        IsSupported: true,
        IsEnabled: true,
        IsWideColorGamutEnabled: false,
        IsForceDisabled: false,
        ColorEncoding: "RGB",
        BitsPerColorChannel: 10);

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

    private static double CombinedOpacity(double baseOpacity, double additionalOpacity) =>
        1 - ((1 - baseOpacity) * (1 - additionalOpacity));

    private static LumaBrakeSettings ManualOnlySettings() =>
        new()
        {
            ManualDimmingOpacity = 0.25,
            SpikeGuard = new SpikeDetectorOptions { MinimumActivationLuminance = 2 },
            FlashGuard = new FlashDetectorOptions { Enabled = false },
            StaticShade = new StaticShadeOptions { Enabled = false },
            AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
            InactiveMonitor = new InactiveMonitorOptions { Enabled = false }
        };

    private static LumaBrakeSettings NightColorOnlySettings() =>
        new()
        {
            NightColor = new NightColorOptions
            {
                Enabled = true,
                ScheduleMode = NightColorScheduleMode.Manual,
                ManualTemperatureKelvin = 3400,
                Brightness = 0.75,
                TintStrength = 1.0
            },
            SpikeGuard = new SpikeDetectorOptions { Enabled = false },
            FlashGuard = new FlashDetectorOptions { Enabled = false },
            StaticShade = new StaticShadeOptions { Enabled = false },
            AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
            InactiveMonitor = new InactiveMonitorOptions { Enabled = false }
        };

    private static LumaBrakeSettings AutomaticSpikeOnlySettings() =>
        new()
        {
            FlashGuard = new FlashDetectorOptions { Enabled = false },
            StaticShade = new StaticShadeOptions { Enabled = false },
            AdaptiveContent = new AdaptiveContentOptions { Enabled = false },
            InactiveMonitor = new InactiveMonitorOptions { Enabled = false }
        };

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), TestContext.Current.CancellationToken);
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

        public Exception? Failure { get; set; }

        public IReadOnlyList<MonitorDescriptor> GetMonitors()
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            return Monitors;
        }
    }

    private sealed class ThrowingMonitorCatalog : IMonitorCatalog
    {
        private readonly Exception _failure;

        public ThrowingMonitorCatalog(Exception failure)
        {
            _failure = failure;
        }

        public IReadOnlyList<MonitorDescriptor> GetMonitors() => throw _failure;
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

    private sealed class FlakyFocusedMonitorProvider : IFocusedMonitorProvider
    {
        private readonly string _firstStableId;
        private int _calls;

        public FlakyFocusedMonitorProvider(string firstStableId)
        {
            _firstStableId = firstStableId;
        }

        public string? FocusedMonitorStableId(IReadOnlyList<MonitorDescriptor> monitors) =>
            Interlocked.Increment(ref _calls) == 1 ? _firstStableId : null;
    }

    private sealed class ThrowingFocusedMonitorProvider : IFocusedMonitorProvider
    {
        public static readonly ThrowingFocusedMonitorProvider Instance = new();

        public string? FocusedMonitorStableId(IReadOnlyList<MonitorDescriptor> monitors) =>
            throw new InvalidOperationException("Focus lookup failed.");
    }

    private sealed class MutableCursorMonitorProvider : ICursorMonitorProvider
    {
        public MutableCursorMonitorProvider(string? cursorStableId)
        {
            CursorStableId = cursorStableId;
        }

        public string? CursorStableId { get; set; }

        public string? CursorMonitorStableId(IReadOnlyList<MonitorDescriptor> monitors) => CursorStableId;
    }

    private sealed class ThrowingCursorMonitorProvider : ICursorMonitorProvider
    {
        public static readonly ThrowingCursorMonitorProvider Instance = new();

        public string? CursorMonitorStableId(IReadOnlyList<MonitorDescriptor> monitors) =>
            throw new InvalidOperationException("Cursor lookup failed.");
    }

    private sealed class FixedForegroundApplicationProvider : IForegroundApplicationProvider
    {
        private readonly ForegroundApplicationInfo _application;

        public FixedForegroundApplicationProvider(ForegroundApplicationInfo application)
        {
            _application = application;
        }

        public ForegroundApplicationInfo Current() => _application;
    }

    private sealed class RecordingNightColorDisplayController : INightColorDisplayController
    {
        private readonly bool _applyResult;

        public RecordingNightColorDisplayController(bool applyResult)
        {
            _applyResult = applyResult;
        }

        public int ApplyCalls { get; private set; }

        public int ClearCalls { get; private set; }

        public NightColorState? LastState { get; private set; }

        public bool Apply(IReadOnlyList<MonitorDescriptor> monitors, NightColorState state)
        {
            ApplyCalls++;
            LastState = state;
            return _applyResult;
        }

        public void Clear()
        {
            ClearCalls++;
        }

        public void Dispose()
        {
        }
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

    private sealed class CoordinatedFirstPassCapture : IFrameCapture
    {
        private readonly ConcurrentDictionary<string, int> _captureCounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly CountdownEvent _firstPassEntered;
        private readonly ManualResetEventSlim _release = new();

        public CoordinatedFirstPassCapture(int expectedFirstPasses)
        {
            _firstPassEntered = new CountdownEvent(expectedFirstPasses);
        }

        public CapturedFrame Capture(MonitorDescriptor monitor)
        {
            var count = _captureCounts.AddOrUpdate(monitor.StableId, 1, (_, current) => current + 1);
            if (count == 1)
            {
                _firstPassEntered.Signal();
                _release.Wait(TimeSpan.FromSeconds(5));
            }

            return new CapturedFrame(
                SolidPixels(32),
                new FrameMetadata(DateTimeOffset.UtcNow, 160, 90, monitor.DeviceName),
                monitor);
        }

        public bool WaitForFirstPass(TimeSpan timeout) => _firstPassEntered.Wait(timeout);

        public int CaptureCount(string stableId) => _captureCounts.TryGetValue(stableId, out var count) ? count : 0;

        public void Release() => _release.Set();

        public void Dispose()
        {
            _release.Set();
            _release.Dispose();
            _firstPassEntered.Dispose();
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

    private sealed class MovingTileActivityCapture : IFrameCapture
    {
        private readonly string _movingStableId;
        private readonly ConcurrentDictionary<string, int> _captureCounts = new(StringComparer.OrdinalIgnoreCase);

        public MovingTileActivityCapture(string movingStableId)
        {
            _movingStableId = movingStableId;
        }

        public CapturedFrame Capture(MonitorDescriptor monitor)
        {
            var count = _captureCounts.AddOrUpdate(monitor.StableId, 1, (_, current) => current + 1);
            var moves = string.Equals(monitor.StableId, _movingStableId, StringComparison.OrdinalIgnoreCase);
            return new CapturedFrame(
                moves ? MovingTilePixels(count % 2 == 0) : SolidPixels(128),
                new FrameMetadata(DateTimeOffset.UtcNow, 160, 90, monitor.DeviceName),
                monitor);
        }

        public void Dispose()
        {
        }

        private static byte[] MovingTilePixels(bool rightSide)
        {
            var pixels = SolidPixels(128);
            var startTileColumn = rightSide ? 8 : 0;
            for (var tileColumn = startTileColumn; tileColumn < startTileColumn + 8; tileColumn++)
            {
                for (var y = 0; y < 10; y++)
                {
                    for (var x = tileColumn * 10; x < (tileColumn + 1) * 10; x++)
                    {
                        var offset = ((y * 160) + x) * 4;
                        pixels[offset] = 255;
                        pixels[offset + 1] = 255;
                        pixels[offset + 2] = 255;
                    }
                }
            }

            return pixels;
        }
    }

    private sealed class MutableBrightnessCapture : IFrameCapture
    {
        private int _brightness;

        public MutableBrightnessCapture(byte brightness)
        {
            _brightness = brightness;
        }

        public void SetBrightness(byte brightness) => Volatile.Write(ref _brightness, brightness);

        public CapturedFrame Capture(MonitorDescriptor monitor) =>
            new(
                SolidPixels((byte)Volatile.Read(ref _brightness)),
                new FrameMetadata(DateTimeOffset.UtcNow, 160, 90, monitor.DeviceName),
                monitor);

        public void Dispose()
        {
        }
    }

    private sealed class BrightnessSequenceCapture : IFrameCapture
    {
        private readonly byte[] _brightnessValues;
        private readonly ConcurrentDictionary<string, int> _captureCounts = new(StringComparer.OrdinalIgnoreCase);

        public BrightnessSequenceCapture(IReadOnlyList<byte> brightnessValues)
        {
            _brightnessValues = brightnessValues.ToArray();
        }

        public CapturedFrame Capture(MonitorDescriptor monitor)
        {
            var count = _captureCounts.AddOrUpdate(monitor.StableId, 1, (_, current) => current + 1);
            var index = Math.Min(count - 1, _brightnessValues.Length - 1);
            return new CapturedFrame(
                SolidPixels(_brightnessValues[index]),
                new FrameMetadata(DateTimeOffset.UtcNow, 160, 90, monitor.DeviceName),
                monitor);
        }

        public int CaptureCount(string stableId) => _captureCounts.TryGetValue(stableId, out var count) ? count : 0;

        public void Dispose()
        {
        }
    }

    private sealed class LocalizedSpikeAfterWarmupCapture : IFrameCapture
    {
        private readonly string _spikeStableId;
        private readonly ConcurrentDictionary<string, int> _captureCounts = new(StringComparer.OrdinalIgnoreCase);

        public LocalizedSpikeAfterWarmupCapture(string spikeStableId)
        {
            _spikeStableId = spikeStableId;
        }

        public CapturedFrame Capture(MonitorDescriptor monitor)
        {
            var count = _captureCounts.AddOrUpdate(monitor.StableId, 1, (_, current) => current + 1);
            var isBright = string.Equals(monitor.StableId, _spikeStableId, StringComparison.OrdinalIgnoreCase) &&
                count >= 4;

            return new CapturedFrame(
                LocalizedPixels(isBright),
                new FrameMetadata(DateTimeOffset.UtcNow, 160, 90, monitor.DeviceName),
                monitor);
        }

        public int CaptureCount(string stableId) => _captureCounts.TryGetValue(stableId, out var count) ? count : 0;

        public void Dispose()
        {
        }

        private static byte[] LocalizedPixels(bool isBright)
        {
            var pixels = SolidPixels(32);
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

    private sealed class BurstFailureCapture : IFrameCapture
    {
        private readonly byte _brightness;
        private int _failuresRemaining;
        private int _failuresTriggered;

        public BurstFailureCapture(byte brightness)
        {
            _brightness = brightness;
        }

        public int FailuresTriggered => Volatile.Read(ref _failuresTriggered);

        public void FailNext(int failures) => Interlocked.Exchange(ref _failuresRemaining, failures);

        public CapturedFrame Capture(MonitorDescriptor monitor)
        {
            while (true)
            {
                var remaining = Volatile.Read(ref _failuresRemaining);
                if (remaining <= 0)
                {
                    break;
                }

                if (Interlocked.CompareExchange(ref _failuresRemaining, remaining - 1, remaining) == remaining)
                {
                    Interlocked.Increment(ref _failuresTriggered);
                    throw new InvalidOperationException("Transient capture failure.");
                }
            }

            return new CapturedFrame(
                SolidPixels(_brightness),
                new FrameMetadata(DateTimeOffset.UtcNow, 160, 90, monitor.DeviceName),
                monitor);
        }

        public void Dispose()
        {
        }
    }

    private sealed class NearEmptyCapture : IFrameCapture
    {
        public CapturedFrame Capture(MonitorDescriptor monitor) =>
            new(
                new byte[160 * 90 * 4],
                new FrameMetadata(DateTimeOffset.UtcNow, 160, 90, monitor.DeviceName),
                monitor);

        public void Dispose()
        {
        }
    }

    private sealed class NearEmptyThenBrightCapture : IFrameCapture
    {
        private readonly string _targetStableId;
        private readonly int _nearEmptyFrames;
        private readonly ConcurrentDictionary<string, int> _captureCounts = new(StringComparer.OrdinalIgnoreCase);

        public NearEmptyThenBrightCapture(string targetStableId, int nearEmptyFrames)
        {
            _targetStableId = targetStableId;
            _nearEmptyFrames = nearEmptyFrames;
        }

        public CapturedFrame Capture(MonitorDescriptor monitor)
        {
            var count = _captureCounts.AddOrUpdate(monitor.StableId, 1, (_, current) => current + 1);
            var nearEmpty = string.Equals(monitor.StableId, _targetStableId, StringComparison.OrdinalIgnoreCase) &&
                count <= _nearEmptyFrames;

            return new CapturedFrame(
                nearEmpty ? new byte[160 * 90 * 4] : SolidPixels(240),
                new FrameMetadata(DateTimeOffset.UtcNow, 160, 90, monitor.DeviceName),
                monitor);
        }

        public int CaptureCount(string stableId) => _captureCounts.TryGetValue(stableId, out var count) ? count : 0;

        public void Dispose()
        {
        }
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

    private sealed class FailsFirstCapture : IFrameCapture
    {
        private readonly RecordingCapture _inner = new();
        private readonly Exception _failure;
        private int _attempts;

        public FailsFirstCapture(Exception? failure = null)
        {
            _failure = failure ?? new InvalidOperationException("Transient capture failure.");
        }

        public int Attempts => _attempts;

        public CapturedFrame Capture(MonitorDescriptor monitor)
        {
            if (Interlocked.Increment(ref _attempts) == 1)
            {
                throw _failure;
            }

            return _inner.Capture(monitor);
        }

        public void Dispose()
        {
            _inner.Dispose();
        }
    }

    private sealed class FailingMonitorCapture : IFrameCapture
    {
        private readonly string _failingStableId;
        private readonly RecordingCapture _inner = new();

        public FailingMonitorCapture(string failingStableId)
        {
            _failingStableId = failingStableId;
        }

        public CapturedFrame Capture(MonitorDescriptor monitor)
        {
            if (string.Equals(monitor.StableId, _failingStableId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Recoverable monitor capture failure.");
            }

            return _inner.Capture(monitor);
        }

        public int CaptureCount(string stableId) => _inner.CaptureCount(stableId);

        public void Dispose()
        {
            _inner.Dispose();
        }
    }

    private sealed class MalformedFrameCapture : IFrameCapture
    {
        public CapturedFrame Capture(MonitorDescriptor monitor) =>
            new(
                [0, 0, 0, 255],
                new FrameMetadata(DateTimeOffset.UtcNow, 160, 90, monitor.DeviceName),
                monitor);

        public void Dispose()
        {
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

    private sealed class LocalizedRedFlashingCapture : IFrameCapture
    {
        private readonly string _flashingStableId;
        private readonly ConcurrentDictionary<string, int> _captureCounts = new(StringComparer.OrdinalIgnoreCase);

        public LocalizedRedFlashingCapture(string flashingStableId)
        {
            _flashingStableId = flashingStableId;
        }

        public CapturedFrame Capture(MonitorDescriptor monitor)
        {
            var count = _captureCounts.AddOrUpdate(monitor.StableId, 1, (_, current) => current + 1);
            var isRed = string.Equals(monitor.StableId, _flashingStableId, StringComparison.OrdinalIgnoreCase) &&
                count % 2 == 0;

            return new CapturedFrame(
                LocalizedPixels(isRed),
                new FrameMetadata(DateTimeOffset.UtcNow, 160, 90, monitor.DeviceName),
                monitor);
        }

        public void Dispose()
        {
        }

        private static byte[] LocalizedPixels(bool isRed)
        {
            var pixels = new byte[160 * 90 * 4];
            for (var index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = 0;
                pixels[index + 1] = 0;
                pixels[index + 2] = 0;
                pixels[index + 3] = 255;
            }

            if (!isRed)
            {
                return pixels;
            }

            for (var y = 0; y < 10; y++)
            {
                for (var x = 0; x < 10; x++)
                {
                    var offset = ((y * 160) + x) * 4;
                    pixels[offset] = 0;
                    pixels[offset + 1] = 0;
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

    private sealed class MutableLocalizedStaticCapture : IFrameCapture
    {
        private readonly string _brightStableId;

        public MutableLocalizedStaticCapture(string brightStableId)
        {
            _brightStableId = brightStableId;
        }

        public bool IsBright { get; set; } = true;

        public CapturedFrame Capture(MonitorDescriptor monitor)
        {
            var isBright = IsBright &&
                string.Equals(monitor.StableId, _brightStableId, StringComparison.OrdinalIgnoreCase);
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

    private sealed class RedFlashingCapture : IFrameCapture
    {
        private readonly string _flashingStableId;
        private readonly ConcurrentDictionary<string, int> _captureCounts = new(StringComparer.OrdinalIgnoreCase);

        public RedFlashingCapture(string flashingStableId)
        {
            _flashingStableId = flashingStableId;
        }

        public CapturedFrame Capture(MonitorDescriptor monitor)
        {
            var count = _captureCounts.AddOrUpdate(monitor.StableId, 1, (_, current) => current + 1);
            var isRed = string.Equals(monitor.StableId, _flashingStableId, StringComparison.OrdinalIgnoreCase) &&
                count % 2 == 0;

            return new CapturedFrame(
                SolidPixels(isRed),
                new FrameMetadata(DateTimeOffset.UtcNow, 160, 90, monitor.DeviceName),
                monitor);
        }

        public void Dispose()
        {
        }

        private static byte[] SolidPixels(bool isRed)
        {
            var pixels = new byte[160 * 90 * 4];
            for (var index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = 0;
                pixels[index + 1] = 0;
                pixels[index + 2] = isRed ? (byte)255 : (byte)0;
                pixels[index + 3] = 255;
            }

            return pixels;
        }
    }

    private sealed class RecordingOverlay : IOverlayRenderer
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, FinalEffectState> _effects = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string MonitorStableId, FinalEffectState Effect)> _history = [];

        public IReadOnlyList<string> LastSyncedStableIds { get; private set; } = [];

        public int HideAllCalls { get; private set; }

        public int HideFailures { get; private set; }

        public bool ThrowOnHideAll { get; init; }

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
                _history.Add((monitorStableId, effect));
            }
        }

        public void HideAll()
        {
            lock (_gate)
            {
                HideAllCalls++;
                if (ThrowOnHideAll)
                {
                    HideFailures++;
                    throw new InvalidOperationException("Overlay cleanup failed.");
                }
            }
        }

        public FinalEffectState? LastEffect(string monitorStableId)
        {
            lock (_gate)
            {
                return _effects.GetValueOrDefault(monitorStableId);
            }
        }

        public IReadOnlyList<FinalEffectState> EffectsFor(string monitorStableId)
        {
            lock (_gate)
            {
                return _history
                    .Where(entry => string.Equals(entry.MonitorStableId, monitorStableId, StringComparison.OrdinalIgnoreCase))
                    .Select(entry => entry.Effect)
                    .ToArray();
            }
        }

        public void ClearEffectHistory()
        {
            lock (_gate)
            {
                _history.Clear();
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed class TransientRenderFailureOverlay : IOverlayRenderer
    {
        private readonly RecordingOverlay _inner = new();
        private int _setEffectFailuresRemaining = 1;
        private int _failNextHideAll;
        private int _hideFailures;
        private int _setEffectAttempts;

        public int HideFailures => Volatile.Read(ref _hideFailures);

        public int SetEffectAttempts => Volatile.Read(ref _setEffectAttempts);

        public void SyncMonitors(IReadOnlyList<MonitorDescriptor> monitors) => _inner.SyncMonitors(monitors);

        public void Show() => _inner.Show();

        public void SetEffect(string monitorStableId, FinalEffectState effect)
        {
            Interlocked.Increment(ref _setEffectAttempts);
            if (Interlocked.Decrement(ref _setEffectFailuresRemaining) == 0)
            {
                Interlocked.Exchange(ref _failNextHideAll, 1);
                throw new OverlayRenderSurfaceLostException("Render surface was lost.");
            }

            _inner.SetEffect(monitorStableId, effect);
        }

        public void HideAll()
        {
            if (Interlocked.Exchange(ref _failNextHideAll, 0) == 1)
            {
                Interlocked.Increment(ref _hideFailures);
                throw new InvalidOperationException("Overlay cleanup failed.");
            }

            _inner.HideAll();
        }

        public void Dispose() => _inner.Dispose();
    }
}
