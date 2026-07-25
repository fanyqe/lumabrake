using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Monitors;

namespace LumaBrake.Rendering;

internal interface IOverlayWindow : IDisposable
{
    MonitorDescriptor Monitor { get; }

    bool IsVisible { get; }

    void Show();

    void UpdateMonitor(MonitorDescriptor monitor);

    void SetEffect(FinalEffectState effect);

    void HideOverlay();
}

internal sealed class OverlayWindow : Window, IOverlayWindow
{
    private const double RegionalFeatherRadius = 18;
    private const double RegionalIdentityTolerance = 0.035;
    private const double RegionalMinimumOverlapRatio = 0.08;
    private const double RegionalMaximumAnimatedCenterShift = 0.16;
    private static readonly Duration RegionalMotionAnimationDuration = new(TimeSpan.FromMilliseconds(90));
    private static readonly Duration RegionalOpacityAnimationDuration = new(TimeSpan.FromMilliseconds(80));

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
            if (rectangle.Tag is RegionalOverlayTag { Region: var region })
            {
                StopRegionAnimations(rectangle);
                ApplyRegion(rectangle, region, width, height);
            }
        }
    }

    private void UpdateRegionalOverlays(IReadOnlyList<RegionalEffectMask> masks)
    {
        var activeMasks = masks
            .Where(mask => mask.Opacity > 0.001 && mask.Region.Area > 0)
            .ToArray();

        var assignments = AssignRegionalOverlays(activeMasks);
        while (_regionalOverlays.Count < activeMasks.Length || assignments.Values.Any(index => index >= _regionalOverlays.Count))
        {
            AddRegionalOverlay();
        }

        var width = EffectiveWidth();
        var height = EffectiveHeight();
        for (var index = 0; index < activeMasks.Length; index++)
        {
            var mask = activeMasks[index];
            var rectangle = _regionalOverlays[assignments[index]];
            var region = mask.Region.Clamp();
            var previousTag = rectangle.Tag as RegionalOverlayTag;
            var previousVisible = rectangle.Visibility == Visibility.Visible;
            var opacity = Math.Clamp(mask.Opacity, 0, 0.92);

            rectangle.Tag = new RegionalOverlayTag(mask.Reason, region, Retiring: false);
            rectangle.Visibility = Visibility.Visible;
            if (previousVisible && previousTag is not null)
            {
                AnimateRegion(rectangle, region, width, height);
                AnimateProperty(rectangle, UIElement.OpacityProperty, opacity, RegionalOpacityAnimationDuration);
            }
            else
            {
                StopRegionAnimations(rectangle);
                rectangle.BeginAnimation(UIElement.OpacityProperty, null);
                rectangle.Opacity = 0;
                ApplyRegion(rectangle, region, width, height);
                AnimateProperty(rectangle, UIElement.OpacityProperty, opacity, RegionalOpacityAnimationDuration);
            }
        }

        var assignedOverlayIndexes = assignments.Values.ToHashSet();
        for (var index = 0; index < _regionalOverlays.Count; index++)
        {
            if (!assignedOverlayIndexes.Contains(index))
            {
                RetireRegionalOverlay(_regionalOverlays[index]);
            }
        }
    }

    private Dictionary<int, int> AssignRegionalOverlays(IReadOnlyList<RegionalEffectMask> activeMasks)
    {
        var assignments = new Dictionary<int, int>();
        var usedOverlayIndexes = new HashSet<int>();
        for (var maskIndex = 0; maskIndex < activeMasks.Count; maskIndex++)
        {
            var overlayIndex = BestReusableOverlayIndex(activeMasks[maskIndex], usedOverlayIndexes);
            if (overlayIndex is null)
            {
                continue;
            }

            assignments[maskIndex] = overlayIndex.Value;
            usedOverlayIndexes.Add(overlayIndex.Value);
        }

        for (var maskIndex = 0; maskIndex < activeMasks.Count; maskIndex++)
        {
            if (assignments.ContainsKey(maskIndex))
            {
                continue;
            }

            var overlayIndex = FirstReusableOverlayIndex(usedOverlayIndexes);
            if (overlayIndex is null)
            {
                overlayIndex = _regionalOverlays.Count;
                AddRegionalOverlay();
            }

            assignments[maskIndex] = overlayIndex.Value;
            usedOverlayIndexes.Add(overlayIndex.Value);
        }

        return assignments;
    }

    private int? BestReusableOverlayIndex(RegionalEffectMask mask, HashSet<int> usedOverlayIndexes)
    {
        var bestScore = double.NegativeInfinity;
        int? bestIndex = null;
        for (var index = 0; index < _regionalOverlays.Count; index++)
        {
            if (usedOverlayIndexes.Contains(index) ||
                _regionalOverlays[index].Visibility != Visibility.Visible ||
                _regionalOverlays[index].Tag is not RegionalOverlayTag tag ||
                !CanReuseRegionalOverlay(tag, mask))
            {
                continue;
            }

            var score = RegionalReuseScore(tag.Region, mask.Region);
            if (tag.Retiring)
            {
                score -= 0.05;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private int? FirstReusableOverlayIndex(HashSet<int> usedOverlayIndexes)
    {
        for (var index = 0; index < _regionalOverlays.Count; index++)
        {
            if (!usedOverlayIndexes.Contains(index) && _regionalOverlays[index].Visibility != Visibility.Visible)
            {
                return index;
            }
        }

        return null;
    }

    private void AddRegionalOverlay()
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
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        _regionalOverlays.Add(rectangle);
        _surface.Children.Add(rectangle);
    }

    private void RetireRegionalOverlay(Rectangle rectangle)
    {
        if (rectangle.Visibility != Visibility.Visible || rectangle.Tag is not RegionalOverlayTag tag)
        {
            return;
        }

        rectangle.Tag = tag with { Retiring = true };
        AnimateProperty(rectangle, UIElement.OpacityProperty, 0, RegionalOpacityAnimationDuration, (_, _) =>
        {
            if (rectangle.Tag is RegionalOverlayTag { Retiring: true })
            {
                StopRegionAnimations(rectangle);
                rectangle.BeginAnimation(UIElement.OpacityProperty, null);
                rectangle.Tag = null;
                rectangle.Opacity = 0;
                rectangle.Visibility = Visibility.Collapsed;
                HideIfEmpty();
            }
        });
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
        _regionalOverlays.Count(rectangle => rectangle.Visibility == Visibility.Visible && rectangle.Tag is RegionalOverlayTag);

    private void HideIfEmpty()
    {
        if (_globalOverlay.Opacity <= 0.001 && _tintOverlay.Opacity <= 0.001 && ActiveRegionalOverlayCount() == 0)
        {
            Hide();
        }
    }

    private void ApplyRegion(Rectangle rectangle, EffectRegion region, double width, double height)
    {
        rectangle.Width = region.Width * width;
        rectangle.Height = region.Height * height;
        Canvas.SetLeft(rectangle, region.X * width);
        Canvas.SetTop(rectangle, region.Y * height);
    }

    private static void AnimateRegion(Rectangle rectangle, EffectRegion region, double width, double height)
    {
        AnimateProperty(rectangle, FrameworkElement.WidthProperty, region.Width * width, RegionalMotionAnimationDuration);
        AnimateProperty(rectangle, FrameworkElement.HeightProperty, region.Height * height, RegionalMotionAnimationDuration);
        AnimateProperty(rectangle, Canvas.LeftProperty, region.X * width, RegionalMotionAnimationDuration);
        AnimateProperty(rectangle, Canvas.TopProperty, region.Y * height, RegionalMotionAnimationDuration);
    }

    private static void AnimateProperty(
        FrameworkElement target,
        DependencyProperty property,
        double to,
        Duration duration,
        EventHandler? completed = null)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = duration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        if (completed is not null)
        {
            animation.Completed += completed;
        }

        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void StopRegionAnimations(Rectangle rectangle)
    {
        rectangle.BeginAnimation(FrameworkElement.WidthProperty, null);
        rectangle.BeginAnimation(FrameworkElement.HeightProperty, null);
        rectangle.BeginAnimation(Canvas.LeftProperty, null);
        rectangle.BeginAnimation(Canvas.TopProperty, null);
    }

    private double EffectiveWidth() => ActualWidth > 0 ? ActualWidth : Math.Max(0, Width);

    private double EffectiveHeight() => ActualHeight > 0 ? ActualHeight : Math.Max(0, Height);

    private static bool CanReuseRegionalOverlay(RegionalOverlayTag tag, RegionalEffectMask mask)
    {
        if (tag.Reason != mask.Reason)
        {
            return false;
        }

        var previous = tag.Region.Clamp();
        var current = mask.Region.Clamp();
        var overlapRatio = RegionalOverlapRatio(previous, current);
        if (overlapRatio >= RegionalMinimumOverlapRatio || previous.TouchesOrOverlaps(current, RegionalIdentityTolerance))
        {
            return true;
        }

        return RegionSizeSimilarity(previous, current) >= 0.45 &&
            RegionalCenterDistance(previous, current) <= RegionalMaximumAnimatedCenterShift;
    }

    private static double RegionalReuseScore(EffectRegion previous, EffectRegion current) =>
        (RegionalOverlapRatio(previous, current) * 4) - RegionalCenterDistance(previous, current);

    private static double RegionalOverlapRatio(EffectRegion first, EffectRegion second)
    {
        var firstClamped = first.Clamp();
        var secondClamped = second.Clamp();
        var x = Math.Max(firstClamped.X, secondClamped.X);
        var y = Math.Max(firstClamped.Y, secondClamped.Y);
        var right = Math.Min(firstClamped.Right, secondClamped.Right);
        var bottom = Math.Min(firstClamped.Bottom, secondClamped.Bottom);
        var intersectionArea = Math.Max(0, right - x) * Math.Max(0, bottom - y);
        var referenceArea = Math.Min(firstClamped.Area, secondClamped.Area);
        return referenceArea <= 0 ? 0 : intersectionArea / referenceArea;
    }

    private static double RegionSizeSimilarity(EffectRegion first, EffectRegion second)
    {
        var firstClamped = first.Clamp();
        var secondClamped = second.Clamp();
        return Math.Min(SizeRatio(firstClamped.Width, secondClamped.Width), SizeRatio(firstClamped.Height, secondClamped.Height));
    }

    private static double SizeRatio(double first, double second)
    {
        var larger = Math.Max(first, second);
        if (larger <= 0)
        {
            return 0;
        }

        return Math.Min(first, second) / larger;
    }

    private static double RegionalCenterDistance(EffectRegion first, EffectRegion second)
    {
        var firstCenterX = first.X + (first.Width / 2);
        var firstCenterY = first.Y + (first.Height / 2);
        var secondCenterX = second.X + (second.Width / 2);
        var secondCenterY = second.Y + (second.Height / 2);
        var x = firstCenterX - secondCenterX;
        var y = firstCenterY - secondCenterY;
        return Math.Sqrt((x * x) + (y * y));
    }

    private sealed record RegionalOverlayTag(EffectReason Reason, EffectRegion Region, bool Retiring);
}
