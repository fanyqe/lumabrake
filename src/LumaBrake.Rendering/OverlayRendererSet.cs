using System.Windows.Threading;
using System.Runtime.InteropServices;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Monitors;

namespace LumaBrake.Rendering;

public sealed class OverlayRendererSet : IOverlayRenderer
{
    private readonly Dispatcher _dispatcher;
    private readonly IOverlayWindowFactory _windowFactory;
    private readonly Dictionary<string, IOverlayWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pendingEffectsGate = new();
    private readonly Dictionary<string, FinalEffectState> _pendingEffects = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _scheduledEffectFlushes = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public OverlayRendererSet(Dispatcher dispatcher)
        : this(dispatcher, new WpfOverlayWindowFactory())
    {
    }

    internal OverlayRendererSet(Dispatcher dispatcher, IOverlayWindowFactory windowFactory)
    {
        _dispatcher = dispatcher;
        _windowFactory = windowFactory;
    }

    public void SyncMonitors(IReadOnlyList<MonitorDescriptor> monitors)
    {
        OnUiThread(() =>
        {
            var activeIds = monitors.Select(monitor => monitor.StableId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var removedId in _windows.Keys.Where(id => !activeIds.Contains(id)).ToArray())
            {
                ClearPendingEffect(removedId);
                RemoveWindow(removedId);
            }

            foreach (var monitor in monitors)
            {
                if (_windows.TryGetValue(monitor.StableId, out var window))
                {
                    try
                    {
                        window.UpdateMonitor(monitor);
                    }
                    catch (Exception exception) when (IsRecoverableRenderFailure(exception))
                    {
                        RecreateWindow(monitor.StableId, monitor, FinalEffectState.Clear);
                    }

                    continue;
                }

                _windows[monitor.StableId] = _windowFactory.Create(monitor);
            }
        });
    }

    public void Show()
    {
        OnUiThread(() =>
        {
            foreach (var entry in _windows.ToArray())
            {
                try
                {
                    if (!entry.Value.IsVisible)
                    {
                        entry.Value.Show();
                    }

                    entry.Value.SetEffect(FinalEffectState.Clear);
                }
                catch (Exception exception) when (IsRecoverableRenderFailure(exception))
                {
                    RecreateWindow(entry.Key, entry.Value.Monitor, FinalEffectState.Clear, showBeforeApply: true);
                }
            }
        });
    }

    public void SetEffect(string monitorStableId, FinalEffectState effect)
    {
        if (_dispatcher.CheckAccess())
        {
            ApplyEffect(monitorStableId, effect);
            return;
        }

        lock (_pendingEffectsGate)
        {
            _pendingEffects[monitorStableId] = effect;
            if (!_scheduledEffectFlushes.Add(monitorStableId))
            {
                return;
            }
        }

        _dispatcher.BeginInvoke(() => FlushPendingEffect(monitorStableId));
    }

    public void HideAll()
    {
        OnUiThread(() =>
        {
            ClearPendingEffects();
            foreach (var entry in _windows.ToArray())
            {
                try
                {
                    entry.Value.HideOverlay();
                }
                catch (Exception exception) when (IsRecoverableRenderFailure(exception))
                {
                    RecreateWindow(entry.Key, entry.Value.Monitor, FinalEffectState.Clear);
                }
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        OnUiThread(() =>
        {
            ClearPendingEffects();
            foreach (var stableId in _windows.Keys.ToArray())
            {
                RemoveWindow(stableId);
            }
        });
        _disposed = true;
    }

    private void FlushPendingEffect(string monitorStableId)
    {
        FinalEffectState? effect;
        lock (_pendingEffectsGate)
        {
            if (!_pendingEffects.Remove(monitorStableId, out effect))
            {
                _scheduledEffectFlushes.Remove(monitorStableId);
                return;
            }

            _scheduledEffectFlushes.Remove(monitorStableId);
        }

        ApplyEffect(monitorStableId, effect);
    }

    private void ApplyEffect(string monitorStableId, FinalEffectState effect)
    {
        if (_windows.TryGetValue(monitorStableId, out var window))
        {
            try
            {
                window.SetEffect(effect);
            }
            catch (Exception exception) when (IsRecoverableRenderFailure(exception))
            {
                RecreateWindow(monitorStableId, window.Monitor, effect);
            }
        }
    }

    private void RecreateWindow(
        string monitorStableId,
        MonitorDescriptor monitor,
        FinalEffectState effect,
        bool showBeforeApply = false)
    {
        RemoveWindow(monitorStableId);
        var replacement = _windowFactory.Create(monitor);
        _windows[monitorStableId] = replacement;

        try
        {
            if (showBeforeApply && !replacement.IsVisible)
            {
                replacement.Show();
            }

            replacement.SetEffect(FinalEffectState.Clear);
            if (!IsClearEffect(effect))
            {
                replacement.SetEffect(effect);
            }
        }
        catch
        {
            RemoveWindow(monitorStableId);
            throw;
        }
    }

    private void RemoveWindow(string monitorStableId)
    {
        if (!_windows.TryGetValue(monitorStableId, out var window))
        {
            return;
        }

        try
        {
            window.Dispose();
        }
        catch (Exception exception) when (IsRecoverableRenderFailure(exception))
        {
        }

        _windows.Remove(monitorStableId);
    }

    private void ClearPendingEffect(string monitorStableId)
    {
        lock (_pendingEffectsGate)
        {
            _pendingEffects.Remove(monitorStableId);
            _scheduledEffectFlushes.Remove(monitorStableId);
        }
    }

    private void ClearPendingEffects()
    {
        lock (_pendingEffectsGate)
        {
            _pendingEffects.Clear();
            _scheduledEffectFlushes.Clear();
        }
    }

    private static bool IsRecoverableRenderFailure(Exception exception) =>
        exception is OverlayRenderSurfaceLostException or InvalidOperationException or ObjectDisposedException or COMException;

    private static bool IsClearEffect(FinalEffectState effect) =>
        effect.GlobalOpacity <= 0.001 &&
        effect.Tint.Opacity <= 0.001 &&
        effect.RegionalMasks.Count == 0;

    private void OnUiThread(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.BeginInvoke(action);
    }
}

internal interface IOverlayWindowFactory
{
    IOverlayWindow Create(MonitorDescriptor monitor);
}

internal sealed class WpfOverlayWindowFactory : IOverlayWindowFactory
{
    public IOverlayWindow Create(MonitorDescriptor monitor) => new OverlayWindow(monitor);
}
