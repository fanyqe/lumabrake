using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using LumaBrake.Domain.Monitors;

namespace LumaBrake.Rendering;

public sealed class OverlayWindow : Window
{
    public OverlayWindow(MonitorDescriptor monitor)
    {
        Monitor = monitor;
        AllowsTransparency = true;
        Background = Brushes.Black;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStyle = WindowStyle.None;
        Opacity = 0;

        ApplyMonitorBounds(monitor);

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            LumaBrake.Platform.Windows.WindowInterop.MakeOverlayWindow(hwnd);
        };
    }

    public MonitorDescriptor Monitor { get; private set; }

    public void UpdateMonitor(MonitorDescriptor monitor)
    {
        Monitor = monitor;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ApplyMonitorBounds(monitor));
            return;
        }

        ApplyMonitorBounds(monitor);
    }

    public void SetOpacity(double opacity)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetOpacity(opacity));
            return;
        }

        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Opacity = Math.Clamp(opacity, 0, 0.92);
        if (Opacity <= 0.001)
        {
            Hide();
        }
        else if (!IsVisible)
        {
            Show();
        }
    }

    public void HideOverlay()
    {
        SetOpacity(0);
    }

    public void Dispose()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(Dispose);
            return;
        }

        if (!Dispatcher.HasShutdownStarted)
        {
            Close();
        }
    }

    private void ApplyMonitorBounds(MonitorDescriptor monitor)
    {
        var scale = Math.Max(monitor.DpiScale, 0.1);
        Left = monitor.Bounds.Left / scale;
        Top = monitor.Bounds.Top / scale;
        Width = monitor.Bounds.Width / scale;
        Height = monitor.Bounds.Height / scale;
    }
}
