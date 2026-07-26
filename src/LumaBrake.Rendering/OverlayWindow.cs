using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Monitors;

namespace LumaBrake.Rendering;

public sealed class OverlayWindow : Window
{
    private const double RegionalFeatherRadius = 18;

    private readonly Canvas _surface = new()
    {
        IsHitTestVisible = false
    };

    private readonly Rectangle _globalOverlay = new()
    {
        Fill = Brushes.Black,
        IsHitTestVisible = false,
        Opacity = 0
    };

    private readonly Rectangle _tintOverlay = new()
    {
        Fill = Brushes.Transparent,
        IsHitTestVisible = false,
        Opacity = 0
    };

    private readonly List<Rectangle> _regionalOverlays = [];

    public OverlayWindow(MonitorDescriptor monitor)
    {
        Monitor = monitor;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Content = _surface;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStyle = WindowStyle.None;
        Opacity = 1;

        _surface.Children.Add(_globalOverlay);
        _surface.Children.Add(_tintOverlay);

        ApplyMonitorBounds(monitor);
        SizeChanged += (_, _) => UpdateOverlayGeometry();

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

    public void SetEffect(FinalEffectState effect)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetEffect(effect));
            return;
        }

        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _globalOverlay.Opacity = Math.Clamp(effect.GlobalOpacity, 0, 0.92);
        ApplyTint(effect.Tint);
        UpdateRegionalOverlays(effect.RegionalMasks);
        UpdateOverlayGeometry();

        if (_globalOverlay.Opacity <= 0.001 && _tintOverlay.Opacity <= 0.001 && ActiveRegionalOverlayCount() == 0)
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
        SetEffect(FinalEffectState.Clear);
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
        UpdateOverlayGeometry();
    }

    private void UpdateOverlayGeometry()
    {
        var width = EffectiveWidth();
        var height = EffectiveHeight();

        _surface.Width = width;
        _surface.Height = height;
        _globalOverlay.Width = width;
        _globalOverlay.Height = height;
        _tintOverlay.Width = width;
        _tintOverlay.Height = height;

        foreach (var rectangle in _regionalOverlays.Where(rectangle => rectangle.Visibility == Visibility.Visible))
        {
            if (rectangle.Tag is EffectRegion region)
            {
                ApplyRegion(rectangle, region, width, height);
            }
        }
    }

    private void UpdateRegionalOverlays(IReadOnlyList<RegionalEffectMask> masks)
    {
        var activeMasks = masks
            .Where(mask => mask.Opacity > 0.001 && mask.Region.Area > 0)
            .ToArray();

        while (_regionalOverlays.Count < activeMasks.Length)
        {
            var rectangle = new Rectangle
            {
                CacheMode = new BitmapCache(),
                Effect = new BlurEffect
                {
                    Radius = RegionalFeatherRadius,
                    RenderingBias = RenderingBias.Performance
                },
                Fill = Brushes.Black,
                IsHitTestVisible = false
            };
            _regionalOverlays.Add(rectangle);
            _surface.Children.Add(rectangle);
        }

        var width = EffectiveWidth();
        var height = EffectiveHeight();
        for (var index = 0; index < activeMasks.Length; index++)
        {
            var mask = activeMasks[index];
            var rectangle = _regionalOverlays[index];
            var region = mask.Region.Clamp();

            rectangle.Tag = region;
            rectangle.Opacity = Math.Clamp(mask.Opacity, 0, 0.92);
            rectangle.Visibility = Visibility.Visible;
            ApplyRegion(rectangle, region, width, height);
        }

        for (var index = activeMasks.Length; index < _regionalOverlays.Count; index++)
        {
            _regionalOverlays[index].Tag = null;
            _regionalOverlays[index].Opacity = 0;
            _regionalOverlays[index].Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyTint(TintEffect tint)
    {
        var clamped = tint.Clamp();
        if (!clamped.Enabled || clamped.Opacity <= 0.001)
        {
            _tintOverlay.Opacity = 0;
            _tintOverlay.Fill = Brushes.Transparent;
            return;
        }

        _tintOverlay.Fill = new SolidColorBrush(Color.FromRgb(clamped.Red, clamped.Green, clamped.Blue));
        _tintOverlay.Opacity = clamped.Opacity;
    }

    private int ActiveRegionalOverlayCount() =>
        _regionalOverlays.Count(rectangle => rectangle.Visibility == Visibility.Visible && rectangle.Opacity > 0.001);

    private void ApplyRegion(Rectangle rectangle, EffectRegion region, double width, double height)
    {
        rectangle.Width = region.Width * width;
        rectangle.Height = region.Height * height;
        Canvas.SetLeft(rectangle, region.X * width);
        Canvas.SetTop(rectangle, region.Y * height);
    }

    private double EffectiveWidth() => ActualWidth > 0 ? ActualWidth : Math.Max(0, Width);

    private double EffectiveHeight() => ActualHeight > 0 ? ActualHeight : Math.Max(0, Height);
}
