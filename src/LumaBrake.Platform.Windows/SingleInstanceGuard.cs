using System.Threading;

namespace LumaBrake.Platform.Windows;

public sealed class SingleInstanceGuard : IDisposable
{
    public const string DefaultMutexName = @"Local\LumaBrake.App";

    private readonly Mutex _mutex;
    private bool _hasHandle;

    private SingleInstanceGuard(Mutex mutex, bool hasHandle)
    {
        _mutex = mutex;
        _hasHandle = hasHandle;
    }

    public bool HasHandle => _hasHandle;

    public static SingleInstanceGuard Acquire(string mutexName = DefaultMutexName)
    {
        if (string.IsNullOrWhiteSpace(mutexName))
        {
            throw new ArgumentException("Single-instance mutex name is required.", nameof(mutexName));
        }

        var mutex = new Mutex(initiallyOwned: true, name: mutexName, createdNew: out var createdNew);
        return new SingleInstanceGuard(mutex, createdNew);
    }

    public void Dispose()
    {
        if (_hasHandle)
        {
            _mutex.ReleaseMutex();
            _hasHandle = false;
        }

        _mutex.Dispose();
    }
}
