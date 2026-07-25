using System.Windows.Interop;

namespace LumaBrake.Platform.Windows;

public sealed class GlobalHotkey : IDisposable
{
    private readonly int _id;
    private readonly HwndSource _source;
    private readonly Action _onPressed;
    private bool _disposed;

    public GlobalHotkey(int id, Action onPressed)
    {
        _id = id;
        _onPressed = onPressed;

        var parameters = new HwndSourceParameters("LumaBrakeHotkeySink")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        var registered = NativeMethods.RegisterHotKey(
            _source.Handle,
            _id,
            NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModShift,
            (uint)System.Windows.Forms.Keys.L);

        if (!registered)
        {
            throw new InvalidOperationException("Ctrl+Alt+Shift+L hotkey could not be registered.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_source.Handle, _id);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _disposed = true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && wParam.ToInt32() == _id)
        {
            _onPressed();
            handled = true;
        }

        return IntPtr.Zero;
    }
}
