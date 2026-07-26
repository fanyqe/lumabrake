using System.Windows;
using LumaBrake.Capture;
using LumaBrake.Core;
using LumaBrake.Persistence;
using LumaBrake.Platform.Windows;
using LumaBrake.Rendering;
using Forms = System.Windows.Forms;

namespace LumaBrake.App;

public partial class App : System.Windows.Application
{
    private readonly SettingsStore _settingsStore = new();
    private Forms.NotifyIcon? _trayIcon;
    private LumaBrakeController? _controller;
    private GlobalHotkey? _hotkey;
    private MainWindow? _dashboard;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = _settingsStore.Load();
        if (SafeModeSettings.IsRequested(e.Args))
        {
            settings = SafeModeSettings.Apply(settings);
            _settingsStore.Save(settings);
        }
        else
        {
            settings = StartupRestorationPolicy.Apply(settings, DateTimeOffset.Now);
        }

        var overlay = new OverlayRendererSet(Dispatcher);
        _controller = new LumaBrakeController(
            new GdiMonitorCapture(),
            overlay,
            settings,
            new WindowsMonitorEnumerator(),
            new WindowsFocusedMonitorProvider());
        _controller.Start();

        _dashboard = new MainWindow(_controller, _settingsStore);
        _dashboard.Show();

        CreateTrayIcon();
        TryRegisterEmergencyHotkey();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _trayIcon?.Dispose();
        _controller?.Dispose();
        base.OnExit(e);
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open LumaBrake", null, (_, _) => ShowDashboard());
        menu.Items.Add("Enable / Disable", null, (_, _) => ToggleEnabled());
        menu.Items.Add("Emergency disable", null, (_, _) => _controller?.EmergencyDisable());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _trayIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = System.Drawing.SystemIcons.Application,
            Text = "LumaBrake",
            Visible = true
        };

        _trayIcon.DoubleClick += (_, _) => ShowDashboard();
    }

    private void TryRegisterEmergencyHotkey()
    {
        try
        {
            _hotkey = new GlobalHotkey(0x4C42, () => Dispatcher.Invoke(() => _controller?.EmergencyDisable()));
        }
        catch (InvalidOperationException)
        {
            _dashboard?.SetHotkeyStatus("Ctrl+Alt+Shift+L is unavailable.");
        }
    }

    private void ShowDashboard()
    {
        if (_dashboard is null)
        {
            return;
        }

        _dashboard.Show();
        _dashboard.Activate();
    }

    private void ToggleEnabled()
    {
        _controller?.ToggleEnabled();
        if (_controller is not null)
        {
            _settingsStore.Save(_controller.Settings);
        }
    }

    private void ExitApplication()
    {
        _dashboard?.AllowApplicationClose();
        Shutdown();
    }
}
