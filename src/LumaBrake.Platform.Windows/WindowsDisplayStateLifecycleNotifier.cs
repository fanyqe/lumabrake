using Microsoft.Win32;

namespace LumaBrake.Platform.Windows;

public interface IPowerModeChangeSource
{
    void Subscribe(PowerModeChangedEventHandler handler);

    void Unsubscribe(PowerModeChangedEventHandler handler);
}

public interface ISessionSwitchSource
{
    void Subscribe(SessionSwitchEventHandler handler);

    void Unsubscribe(SessionSwitchEventHandler handler);
}

public interface ISessionEndingSource
{
    void Subscribe(SessionEndingEventHandler handler);

    void Unsubscribe(SessionEndingEventHandler handler);
}

public sealed class WindowsDisplayStateLifecycleNotifier : IDisposable
{
    private readonly IPowerModeChangeSource _powerSource;
    private readonly ISessionSwitchSource _sessionSwitchSource;
    private readonly ISessionEndingSource _sessionEndingSource;
    private readonly PowerModeChangedEventHandler _powerHandler;
    private readonly SessionSwitchEventHandler _sessionSwitchHandler;
    private readonly SessionEndingEventHandler _sessionEndingHandler;
    private bool _disposed;

    public WindowsDisplayStateLifecycleNotifier(Action clearDisplayState, Action refreshDisplayState)
        : this(
            clearDisplayState,
            refreshDisplayState,
            SystemPowerModeChangeSource.Instance,
            SystemSessionSwitchSource.Instance,
            SystemSessionEndingSource.Instance)
    {
    }

    public WindowsDisplayStateLifecycleNotifier(
        Action clearDisplayState,
        Action refreshDisplayState,
        IPowerModeChangeSource powerSource,
        ISessionSwitchSource sessionSwitchSource,
        ISessionEndingSource sessionEndingSource)
    {
        ArgumentNullException.ThrowIfNull(clearDisplayState);
        ArgumentNullException.ThrowIfNull(refreshDisplayState);
        ArgumentNullException.ThrowIfNull(powerSource);
        ArgumentNullException.ThrowIfNull(sessionSwitchSource);
        ArgumentNullException.ThrowIfNull(sessionEndingSource);

        _powerSource = powerSource;
        _sessionSwitchSource = sessionSwitchSource;
        _sessionEndingSource = sessionEndingSource;
        _powerHandler = (_, args) =>
        {
            if (_disposed)
            {
                return;
            }

            if (args.Mode == PowerModes.Suspend)
            {
                clearDisplayState();
            }
            else if (args.Mode == PowerModes.Resume)
            {
                refreshDisplayState();
            }
        };
        _sessionSwitchHandler = (_, args) =>
        {
            if (_disposed)
            {
                return;
            }

            if (ShouldClearForSessionSwitch(args.Reason))
            {
                clearDisplayState();
            }
            else if (ShouldRefreshForSessionSwitch(args.Reason))
            {
                refreshDisplayState();
            }
        };
        _sessionEndingHandler = (_, _) =>
        {
            if (!_disposed)
            {
                clearDisplayState();
            }
        };

        _powerSource.Subscribe(_powerHandler);
        _sessionSwitchSource.Subscribe(_sessionSwitchHandler);
        _sessionEndingSource.Subscribe(_sessionEndingHandler);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _powerSource.Unsubscribe(_powerHandler);
        _sessionSwitchSource.Unsubscribe(_sessionSwitchHandler);
        _sessionEndingSource.Unsubscribe(_sessionEndingHandler);
    }

    private static bool ShouldClearForSessionSwitch(SessionSwitchReason reason) =>
        reason is SessionSwitchReason.SessionLock or
            SessionSwitchReason.ConsoleDisconnect or
            SessionSwitchReason.RemoteDisconnect;

    private static bool ShouldRefreshForSessionSwitch(SessionSwitchReason reason) =>
        reason is SessionSwitchReason.SessionUnlock or
            SessionSwitchReason.ConsoleConnect or
            SessionSwitchReason.RemoteConnect;
}

public sealed class SystemPowerModeChangeSource : IPowerModeChangeSource
{
    public static readonly SystemPowerModeChangeSource Instance = new();

    private SystemPowerModeChangeSource()
    {
    }

    public void Subscribe(PowerModeChangedEventHandler handler) =>
        SystemEvents.PowerModeChanged += handler;

    public void Unsubscribe(PowerModeChangedEventHandler handler) =>
        SystemEvents.PowerModeChanged -= handler;
}

public sealed class SystemSessionSwitchSource : ISessionSwitchSource
{
    public static readonly SystemSessionSwitchSource Instance = new();

    private SystemSessionSwitchSource()
    {
    }

    public void Subscribe(SessionSwitchEventHandler handler) =>
        SystemEvents.SessionSwitch += handler;

    public void Unsubscribe(SessionSwitchEventHandler handler) =>
        SystemEvents.SessionSwitch -= handler;
}

public sealed class SystemSessionEndingSource : ISessionEndingSource
{
    public static readonly SystemSessionEndingSource Instance = new();

    private SystemSessionEndingSource()
    {
    }

    public void Subscribe(SessionEndingEventHandler handler) =>
        SystemEvents.SessionEnding += handler;

    public void Unsubscribe(SessionEndingEventHandler handler) =>
        SystemEvents.SessionEnding -= handler;
}
