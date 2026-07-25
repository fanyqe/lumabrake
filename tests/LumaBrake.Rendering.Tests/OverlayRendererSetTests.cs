using System.Windows.Threading;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Shapes;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Monitors;
using LumaBrake.Rendering;

namespace LumaBrake.Rendering.Tests;

public sealed class OverlayRendererSetTests
{
    [Fact]
    public void SetEffect_RecreatesWindowAndReappliesEffectAfterRecoverableFailure()
    {
        var factory = new RecordingOverlayWindowFactory();
        var monitor = Monitor();
        var renderer = new OverlayRendererSet(Dispatcher.CurrentDispatcher, factory);
        var effect = new FinalEffectState(0.32, EffectReason.BrightnessSpike, ["BrightnessSpike"], [], TintEffect.Disabled);

        renderer.SyncMonitors([monitor]);
        factory.Created[0].ThrowOnSetEffect = true;

        renderer.SetEffect(monitor.StableId, effect);

        Assert.Equal(2, factory.Created.Count);
        Assert.True(factory.Created[0].Disposed);
        Assert.Equal([FinalEffectState.Clear, effect], factory.Created[1].Effects);
    }

    [Fact]
    public void SetEffect_RecreatesWindowAndReappliesEffectAfterRenderSurfaceLoss()
    {
        var factory = new RecordingOverlayWindowFactory();
        var monitor = Monitor();
        var renderer = new OverlayRendererSet(Dispatcher.CurrentDispatcher, factory);
        var effect = new FinalEffectState(0.32, EffectReason.BrightnessSpike, ["BrightnessSpike"], [], TintEffect.Disabled);

        renderer.SyncMonitors([monitor]);
        factory.Created[0].SetEffectException = new OverlayRenderSurfaceLostException("Overlay render surface was lost.");

        renderer.SetEffect(monitor.StableId, effect);

        Assert.Equal(2, factory.Created.Count);
        Assert.True(factory.Created[0].Disposed);
        Assert.Equal([FinalEffectState.Clear, effect], factory.Created[1].Effects);
    }

    [Fact]
    public async Task SetEffect_CoalescesQueuedEffectsPerMonitor()
    {
        var completed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var factory = new RecordingOverlayWindowFactory();
                var monitor = Monitor();
                var renderer = new OverlayRendererSet(dispatcher, factory);
                var staleEffect = new FinalEffectState(0.12, EffectReason.AdaptiveContent, ["AdaptiveContent"], [], TintEffect.Disabled);
                var latestEffect = new FinalEffectState(0.40, EffectReason.RepetitiveFlash, ["RepetitiveFlash"], [], TintEffect.Disabled);
                using var workerCompleted = new ManualResetEventSlim();

                renderer.SyncMonitors([monitor]);
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    renderer.SetEffect(monitor.StableId, staleEffect);
                    renderer.SetEffect(monitor.StableId, latestEffect);
                    workerCompleted.Set();
                });

                Assert.True(workerCompleted.Wait(TimeSpan.FromSeconds(2)));
                DrainDispatcher(dispatcher);
                Assert.Equal([latestEffect], factory.Created[0].Effects);

                dispatcher.InvokeShutdown();
                completed.SetResult(null);
            }
            catch (Exception exception)
            {
                completed.SetResult(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var failure = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        if (failure is not null)
        {
            throw failure;
        }
    }

    [Fact]
    public void HideAll_RecreatesFailedWindowInClearState()
    {
        var factory = new RecordingOverlayWindowFactory();
        var monitor = Monitor();
        var renderer = new OverlayRendererSet(Dispatcher.CurrentDispatcher, factory);

        renderer.SyncMonitors([monitor]);
        factory.Created[0].ThrowOnHideOverlay = true;

        renderer.HideAll();

        Assert.Equal(2, factory.Created.Count);
        Assert.True(factory.Created[0].Disposed);
        Assert.Equal([FinalEffectState.Clear], factory.Created[1].Effects);
    }

    [Fact]
    public void SyncMonitors_RecreatesWindowWhenMonitorUpdateFails()
    {
        var factory = new RecordingOverlayWindowFactory();
        var monitor = Monitor();
        var updated = monitor with
        {
            Bounds = new MonitorBounds(0, 0, 320, 180),
            WorkArea = new MonitorBounds(0, 0, 320, 180)
        };
        var renderer = new OverlayRendererSet(Dispatcher.CurrentDispatcher, factory);

        renderer.SyncMonitors([monitor]);
        factory.Created[0].ThrowOnUpdateMonitor = true;
        renderer.SyncMonitors([updated]);

        Assert.Equal(2, factory.Created.Count);
        Assert.True(factory.Created[0].Disposed);
        Assert.Equal(updated, factory.Created[1].Monitor);
        Assert.Equal([FinalEffectState.Clear], factory.Created[1].Effects);
    }

    [Fact]
    public void Dispose_IgnoresRecoverableWindowDisposeFailures()
    {
        var factory = new RecordingOverlayWindowFactory();
        var monitor = Monitor();
        var renderer = new OverlayRendererSet(Dispatcher.CurrentDispatcher, factory);

        renderer.SyncMonitors([monitor]);
        factory.Created[0].ThrowOnDispose = true;

        renderer.Dispose();
        renderer.Dispose();

        Assert.True(factory.Created[0].Disposed);
    }

    [Fact]
    public void SyncMonitors_KeepsWindowTrackedAfterNonRecoverableDisposeFailure()
    {
        var factory = new RecordingOverlayWindowFactory();
        var monitor = Monitor();
        var renderer = new OverlayRendererSet(Dispatcher.CurrentDispatcher, factory);
        var effect = new FinalEffectState(0.24, EffectReason.Manual, ["Manual"], [], TintEffect.Disabled);

        renderer.SyncMonitors([monitor]);
        factory.Created[0].DisposeException = new NotSupportedException("Unexpected teardown failure.");

        Assert.Throws<NotSupportedException>(() => renderer.SyncMonitors([]));
        factory.Created[0].DisposeException = null;
        renderer.SetEffect(monitor.StableId, effect);

        Assert.Equal([effect], factory.Created[0].Effects);
    }

    [Fact]
    public async Task OverlayWindow_ReusesRegionalOverlayOnlyForNearbyMasks()
    {
        var completed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var window = new OverlayWindow(Monitor());
                var first = new RegionalEffectMask(
                    EffectReason.StaticShade,
                    0.28,
                    new EffectRegion(0.10, 0.10, 0.12, 0.12));
                var nearby = new RegionalEffectMask(
                    EffectReason.StaticShade,
                    0.28,
                    new EffectRegion(0.14, 0.13, 0.12, 0.12));
                var far = new RegionalEffectMask(
                    EffectReason.StaticShade,
                    0.28,
                    new EffectRegion(0.68, 0.64, 0.12, 0.12));

                InvokeRegionalOverlayUpdate(window, [first]);
                Assert.Equal(1, RegionalOverlayCount(window));
                Assert.Equal(1, ActiveRegionalOverlayCount(window));

                InvokeRegionalOverlayUpdate(window, [nearby]);
                Assert.Equal(1, RegionalOverlayCount(window));

                InvokeRegionalOverlayUpdate(window, [far]);
                Assert.Equal(2, RegionalOverlayCount(window));

                window.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
                completed.SetResult(null);
            }
            catch (Exception exception)
            {
                completed.SetResult(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var failure = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        if (failure is not null)
        {
            throw failure;
        }
    }

    private static MonitorDescriptor Monitor() =>
        new(
            "display-1",
            @"\\.\DISPLAY1",
            "Primary",
            IsPrimary: true,
            IsActive: true,
            new MonitorBounds(0, 0, 160, 90),
            new MonitorBounds(0, 0, 160, 90));

    private static void DrainDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void InvokeRegionalOverlayUpdate(OverlayWindow window, IReadOnlyList<RegionalEffectMask> masks)
    {
        var method = typeof(OverlayWindow).GetMethod(
            "UpdateRegionalOverlays",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(window, [masks]);
    }

    private static int RegionalOverlayCount(OverlayWindow window)
    {
        var surface = Assert.IsType<Canvas>(window.Content);
        return surface.Children
            .OfType<Rectangle>()
            .Count(rectangle => rectangle.Tag is not null);
    }

    private static int ActiveRegionalOverlayCount(OverlayWindow window)
    {
        var method = typeof(OverlayWindow).GetMethod(
            "ActiveRegionalOverlayCount",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<int>(method.Invoke(window, []));
    }

    private sealed class RecordingOverlayWindowFactory : IOverlayWindowFactory
    {
        public List<RecordingOverlayWindow> Created { get; } = [];

        public IOverlayWindow Create(MonitorDescriptor monitor)
        {
            var window = new RecordingOverlayWindow(monitor);
            Created.Add(window);
            return window;
        }
    }

    private sealed class RecordingOverlayWindow : IOverlayWindow
    {
        public RecordingOverlayWindow(MonitorDescriptor monitor)
        {
            Monitor = monitor;
        }

        public MonitorDescriptor Monitor { get; private set; }

        public bool IsVisible { get; private set; }

        public bool Disposed { get; private set; }

        public bool ThrowOnUpdateMonitor { get; set; }

        public bool ThrowOnSetEffect { get; set; }

        public bool ThrowOnHideOverlay { get; set; }

        public bool ThrowOnDispose { get; set; }

        public Exception? DisposeException { get; set; }

        public Exception? SetEffectException { get; set; }

        public List<FinalEffectState> Effects { get; } = [];

        public void Show() => IsVisible = true;

        public void UpdateMonitor(MonitorDescriptor monitor)
        {
            if (ThrowOnUpdateMonitor)
            {
                throw new InvalidOperationException("Display surface was lost.");
            }

            Monitor = monitor;
        }

        public void SetEffect(FinalEffectState effect)
        {
            if (SetEffectException is not null)
            {
                var exception = SetEffectException;
                SetEffectException = null;
                throw exception;
            }

            if (ThrowOnSetEffect)
            {
                ThrowOnSetEffect = false;
                throw new InvalidOperationException("Render surface was lost.");
            }

            Effects.Add(effect);
            IsVisible = effect.GlobalOpacity > 0.001 || effect.Tint.Opacity > 0.001 || effect.RegionalMasks.Count > 0;
        }

        public void HideOverlay()
        {
            if (ThrowOnHideOverlay)
            {
                throw new InvalidOperationException("Render surface was lost.");
            }

            SetEffect(FinalEffectState.Clear);
        }

        public void Dispose()
        {
            Disposed = true;
            if (DisposeException is not null)
            {
                throw DisposeException;
            }

            if (ThrowOnDispose)
            {
                throw new InvalidOperationException("Render surface was already gone.");
            }
        }
    }
}
