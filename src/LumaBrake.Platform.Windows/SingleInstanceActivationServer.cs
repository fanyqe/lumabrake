using System.Threading;

namespace LumaBrake.Platform.Windows;

public sealed class SingleInstanceActivationServer : IDisposable
{
    public const string DefaultEventName = @"Local\LumaBrake.App.Activate";

    private readonly EventWaitHandle _activationEvent;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Thread _listenerThread;
    private bool _disposed;

    private SingleInstanceActivationServer(EventWaitHandle activationEvent, Action activate)
    {
        _activationEvent = activationEvent;
        _listenerThread = new Thread(() => Listen(activate))
        {
            IsBackground = true,
            Name = "LumaBrake single-instance activation listener"
        };
        _listenerThread.Start();
    }

    public static SingleInstanceActivationServer Start(
        Action activate,
        string eventName = DefaultEventName)
    {
        ArgumentNullException.ThrowIfNull(activate);
        if (string.IsNullOrWhiteSpace(eventName))
        {
            throw new ArgumentException("Activation event name is required.", nameof(eventName));
        }

        var activationEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: eventName);
        return new SingleInstanceActivationServer(activationEvent, activate);
    }

    public static bool NotifyExistingInstance(string eventName = DefaultEventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            throw new ArgumentException("Activation event name is required.", nameof(eventName));
        }

        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(eventName);
            activationEvent.Set();
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _shutdown.Cancel();
        _activationEvent.Set();
        _listenerThread.Join(TimeSpan.FromSeconds(2));
        _activationEvent.Dispose();
        _shutdown.Dispose();
        _disposed = true;
    }

    private void Listen(Action activate)
    {
        while (true)
        {
            _activationEvent.WaitOne();
            if (_shutdown.IsCancellationRequested)
            {
                return;
            }

            activate();
        }
    }
}
