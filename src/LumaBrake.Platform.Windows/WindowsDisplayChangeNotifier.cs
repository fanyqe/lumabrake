using Microsoft.Win32;

namespace LumaBrake.Platform.Windows;

public interface IDisplaySettingsChangeSource
{
    void Subscribe(EventHandler handler);

    void Unsubscribe(EventHandler handler);
}

public sealed class WindowsDisplayChangeNotifier : IDisposable
{
    private readonly IDisplaySettingsChangeSource _source;
    private readonly EventHandler _handler;
    private bool _disposed;

    public WindowsDisplayChangeNotifier(Action refreshMonitors)
        : this(refreshMonitors, SystemDisplaySettingsChangeSource.Instance)
    {
    }

    public WindowsDisplayChangeNotifier(Action refreshMonitors, IDisplaySettingsChangeSource source)
    {
        ArgumentNullException.ThrowIfNull(refreshMonitors);
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
        _handler = (_, _) =>
        {
            if (!_disposed)
            {
                refreshMonitors();
            }
        };

        _source.Subscribe(_handler);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.Unsubscribe(_handler);
    }
}

public sealed class SystemDisplaySettingsChangeSource : IDisplaySettingsChangeSource
{
    public static readonly SystemDisplaySettingsChangeSource Instance = new();

    private SystemDisplaySettingsChangeSource()
    {
    }

    public void Subscribe(EventHandler handler) =>
        SystemEvents.DisplaySettingsChanged += handler;

    public void Unsubscribe(EventHandler handler) =>
        SystemEvents.DisplaySettingsChanged -= handler;
}
