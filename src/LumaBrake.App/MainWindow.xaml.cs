using System.Windows;
using LumaBrake.Core;
using LumaBrake.Diagnostics;
using LumaBrake.Persistence;

namespace LumaBrake.App;

public partial class MainWindow : Window
{
    private readonly LumaBrakeController _controller;
    private readonly SettingsStore _settingsStore;
    private bool _allowApplicationClose;

    public MainWindow(LumaBrakeController controller, SettingsStore settingsStore)
    {
        _controller = controller;
        _settingsStore = settingsStore;
        InitializeComponent();

        MaximumOpacitySlider.Value = _controller.Settings.UserMaximumOpacity;
        _controller.SnapshotUpdated += ControllerOnSnapshotUpdated;
        RenderSnapshot(_controller.Snapshot());
    }

    public void SetHotkeyStatus(string status)
    {
        HotkeyText.Text = status;
    }

    public void AllowApplicationClose()
    {
        _allowApplicationClose = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _controller.SnapshotUpdated -= ControllerOnSnapshotUpdated;
        base.OnClosed(e);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowApplicationClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void ControllerOnSnapshotUpdated(object? sender, PipelineSnapshot snapshot)
    {
        Dispatcher.Invoke(() => RenderSnapshot(snapshot));
    }

    private void ToggleButton_OnClick(object sender, RoutedEventArgs e)
    {
        _controller.ToggleEnabled();
        _settingsStore.Save(_controller.Settings);
        RenderSnapshot(_controller.Snapshot());
    }

    private void EmergencyButton_OnClick(object sender, RoutedEventArgs e)
    {
        _controller.EmergencyDisable();
        RenderSnapshot(_controller.Snapshot());
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        _settingsStore.Save(_controller.Settings);
        FooterText.Text = "Settings saved.";
    }

    private void RefreshMonitorsButton_OnClick(object sender, RoutedEventArgs e)
    {
        _controller.RefreshMonitors();
        RenderSnapshot(_controller.Snapshot());
    }

    private void MaximumOpacitySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized)
        {
            return;
        }

        _controller.SetUserMaximumOpacity(e.NewValue);
        MaximumOpacityText.Text = FormatPercent(e.NewValue);
    }

    private void RenderSnapshot(PipelineSnapshot snapshot)
    {
        StateText.Text = snapshot.IsEnabled ? "Enabled" : "Disabled";
        ToggleButton.Content = snapshot.IsEnabled ? "Disable" : "Enable";
        AverageText.Text = snapshot.AverageLuminance.ToString("0.000");
        P95Text.Text = snapshot.Percentile95Luminance.ToString("0.000");
        OpacityText.Text = FormatPercent(snapshot.OverlayOpacity);
        EventsText.Text = snapshot.SpikeEvents.ToString("N0");
        MaximumOpacityText.Text = FormatPercent(MaximumOpacitySlider.Value);
        MonitorText.Text = snapshot.MonitorCount == 0
            ? snapshot.MonitorStatus
            : $"{snapshot.ActiveMonitor}, 160 x 90 BGRA sample, {snapshot.MonitorStatus}";
    }

    private static string FormatPercent(double value) => $"{Math.Round(value * 100):0}%";
}
