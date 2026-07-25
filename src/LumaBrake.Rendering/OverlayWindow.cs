using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LumaBrake.Rendering;

public sealed class OverlayWindow : Window, IOverlayRenderer
{
    public OverlayWindow()
    {
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

        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            LumaBrake.Platform.Windows.WindowInterop.MakeOverlayWindow(hwnd);
        };
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
}
