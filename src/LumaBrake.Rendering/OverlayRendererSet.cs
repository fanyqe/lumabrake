using System.Windows.Threading;
using LumaBrake.Domain.Monitors;

namespace LumaBrake.Rendering;

public sealed class OverlayRendererSet : IOverlayRenderer
{
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, OverlayWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public OverlayRendererSet(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void SyncMonitors(IReadOnlyList<MonitorDescriptor> monitors)
    {
        OnUiThread(() =>
        {
            var activeIds = monitors.Select(monitor => monitor.StableId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var removedId in _windows.Keys.Where(id => !activeIds.Contains(id)).ToArray())
            {
                _windows[removedId].Dispose();
                _windows.Remove(removedId);
            }

            foreach (var monitor in monitors)
            {
                if (_windows.TryGetValue(monitor.StableId, out var window))
                {
                    window.UpdateMonitor(monitor);
                    continue;
                }

                _windows[monitor.StableId] = new OverlayWindow(monitor);
            }
        });
    }

    public void Show()
    {
        OnUiThread(() =>
        {
            foreach (var window in _windows.Values)
            {
                if (!window.IsVisible)
                {
                    window.Show();
                }

                window.SetOpacity(0);
            }
        });
    }

    public void SetOpacity(string monitorStableId, double opacity)
    {
        OnUiThread(() =>
        {
            if (_windows.TryGetValue(monitorStableId, out var window))
            {
                window.SetOpacity(opacity);
            }
        });
    }

    public void HideAll()
    {
        OnUiThread(() =>
        {
            foreach (var window in _windows.Values)
            {
                window.HideOverlay();
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
            foreach (var window in _windows.Values)
            {
                window.Dispose();
            }

            _windows.Clear();
        });
        _disposed = true;
    }

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
