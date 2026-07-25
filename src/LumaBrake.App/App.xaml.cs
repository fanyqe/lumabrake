using System.IO;
using System.Windows;
using System.Windows.Threading;
using LumaBrake.Capture;
using LumaBrake.Core;
using LumaBrake.Domain.Color;
using LumaBrake.Domain.Hardware;
using LumaBrake.MonitorControl;
using LumaBrake.Persistence;
using LumaBrake.Platform.Windows;
using LumaBrake.Rendering;
using Forms = System.Windows.Forms;

namespace LumaBrake.App;

public partial class App : System.Windows.Application
{
    private static readonly TimeSpan RuntimeHardwareBrightnessPollInterval = TimeSpan.FromSeconds(30);

    private readonly SettingsStore _settingsStore = new();
    private Forms.NotifyIcon? _trayIcon;
    private LumaBrakeController? _controller;
    private GlobalHotkey? _hotkey;
    private MainWindow? _dashboard;
    private SingleInstanceGuard? _singleInstanceGuard;
    private SingleInstanceActivationServer? _activationServer;
    private RuntimeHardwareBrightnessCoordinator? _runtimeHardwareBrightnessCoordinator;
    private CancellationTokenSource? _runtimeHardwareBrightnessShutdown;
    private Task? _runtimeHardwareBrightnessLoop;
    private WindowsDisplayChangeNotifier? _displayChangeNotifier;
    private WindowsDisplayStateLifecycleNotifier? _displayStateLifecycleNotifier;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _singleInstanceGuard = SingleInstanceGuard.Acquire();
        if (!_singleInstanceGuard.HasHandle)
        {
            SingleInstanceActivationServer.NotifyExistingInstance();
            Shutdown();
            return;
        }

        var launchOptions = AppLaunchOptions.FromArgs(e.Args);
        var settings = _settingsStore.Load();
        var storedStartupHardwareBrightness = settings.StartupRestoration.HardwareBrightness.Clamp();
        if (launchOptions.SafeModeRequested)
        {
            settings = SafeModeSettings.Apply(settings);
            _settingsStore.Save(settings);
        }
        else
        {
            settings = StartupRestorationPolicy.Apply(settings, DateTimeOffset.Now);
            settings = ApplyStartupHardwareBrightness(settings, storedStartupHardwareBrightness);
        }

        settings = WithStoredStartupHardwareBrightness(settings, storedStartupHardwareBrightness);

        var overlay = new OverlayRendererSet(Dispatcher);
        _controller = new LumaBrakeController(
            new GdiMonitorCapture(),
            overlay,
            settings,
            new WindowsMonitorEnumerator(),
            new WindowsFocusedMonitorProvider(),
            foregroundApplicationProvider: new WindowsForegroundApplicationProvider(),
            cursorMonitorProvider: new WindowsCursorMonitorProvider(),
            nightColorDisplayController: new WindowsGammaRampNightColorController(_settingsStore.RootDirectory));
        _controller.Start();
        _displayChangeNotifier = new WindowsDisplayChangeNotifier(RefreshMonitorsAfterDisplayChange);
        _displayStateLifecycleNotifier = new WindowsDisplayStateLifecycleNotifier(
            ClearDisplayStateForSystemLifecycle,
            RefreshDisplayStateAfterSystemLifecycle);
        if (!launchOptions.SafeModeRequested)
        {
            StartRuntimeHardwareBrightnessCoordinator(settings);
        }

        _dashboard = new MainWindow(_controller, _settingsStore);
        _activationServer = SingleInstanceActivationServer.Start(() =>
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            Dispatcher.Invoke(ShowDashboard);
        });

        CreateTrayIcon();
        TryRegisterEmergencyHotkey();
        if (launchOptions.SmokeStartupRequested)
        {
            Dispatcher.BeginInvoke(ExitApplication, DispatcherPriority.ApplicationIdle);
            return;
        }

        if (!launchOptions.StartInBackground)
        {
            _dashboard.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _runtimeHardwareBrightnessShutdown?.Cancel();
        try
        {
            _runtimeHardwareBrightnessLoop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _runtimeHardwareBrightnessShutdown?.Dispose();
        _displayStateLifecycleNotifier?.Dispose();
        _displayChangeNotifier?.Dispose();
        _activationServer?.Dispose();
        _hotkey?.Dispose();
        _trayIcon?.Dispose();
        _controller?.Dispose();
        _singleInstanceGuard?.Dispose();
        base.OnExit(e);
    }

    private void RefreshMonitorsAfterDisplayChange()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            Dispatcher.BeginInvoke(RefreshMonitorsForDisplayChange);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ClearDisplayStateForSystemLifecycle()
    {
        try
        {
            _controller?.ClearDisplayStateForSystemEvent();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void RefreshDisplayStateAfterSystemLifecycle()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            Dispatcher.BeginInvoke(RefreshDisplayStateForSystemLifecycle);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void RefreshDisplayStateForSystemLifecycle()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished || _controller is null)
        {
            return;
        }

        try
        {
            _controller.RefreshDisplayStateAfterSystemEvent();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void RefreshMonitorsForDisplayChange()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished || _controller is null)
        {
            return;
        }

        try
        {
            _controller.RefreshMonitors();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void CreateTrayIcon()
    {
        var menu = TrayMenuFactory.Create(
            LoadTrayMenuImage(),
            () => _controller?.Settings.Enabled == true,
            () => _controller is not null && ProtectionTuningPolicy.IsOledProtectionPreset(_controller.Settings),
            NightColorTrayState,
            ShowDashboard,
            ToggleEnabled,
            ApplyOledProtectionPreset,
            () => PauseNightColor(TimeSpan.FromHours(1)),
            ResumeNightColor,
            () => _controller?.EmergencyDisable(),
            ExportDiagnostics,
            ExitApplication);

        _trayIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = LoadTrayIcon(),
            Text = "LumaBrake",
            Visible = true
        };

        _trayIcon.DoubleClick += (_, _) => ShowDashboard();
    }

    private static System.Drawing.Image? LoadTrayMenuImage()
    {
        try
        {
            var streamInfo = GetResourceStream(new Uri("pack://application:,,,/Assets/LumaBrakeTray.png", UriKind.Absolute));
            if (streamInfo?.Stream is null)
            {
                return null;
            }

            using var stream = streamInfo.Stream;
            using var source = System.Drawing.Image.FromStream(stream);
            return new System.Drawing.Bitmap(source, new System.Drawing.Size(28, 28));
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var streamInfo = GetResourceStream(new Uri("pack://application:,,,/Assets/LumaBrakeTray.ico", UriKind.Absolute));
            if (streamInfo?.Stream is null)
            {
                return System.Drawing.SystemIcons.Application;
            }

            using var icon = new System.Drawing.Icon(streamInfo.Stream);
            return (System.Drawing.Icon)icon.Clone();
        }
        catch (ArgumentException)
        {
            return System.Drawing.SystemIcons.Application;
        }
        catch (IOException)
        {
            return System.Drawing.SystemIcons.Application;
        }
        catch (InvalidOperationException)
        {
            return System.Drawing.SystemIcons.Application;
        }
        catch (NotSupportedException)
        {
            return System.Drawing.SystemIcons.Application;
        }
    }

    private LumaBrakeSettings ApplyStartupHardwareBrightness(
        LumaBrakeSettings settings,
        HardwareBrightnessTarget storedStartupHardwareBrightness)
    {
        try
        {
            var restorer = new StartupHardwareBrightnessRestorer(
                new DdcCiBrightnessWriter(),
                new HardwareBrightnessStateStore(rootDirectory: _settingsStore.RootDirectory));
            var result = restorer.Apply(settings, DateTimeOffset.Now);
            if (result.SoftwareFallbackApplied)
            {
                var settingsToSave = WithStoredStartupHardwareBrightness(
                    result.Settings,
                    storedStartupHardwareBrightness);
                _settingsStore.Save(settingsToSave);
                return settingsToSave;
            }

            return result.Settings;
        }
        catch (IOException)
        {
            return settings;
        }
        catch (UnauthorizedAccessException)
        {
            return settings;
        }
        catch (InvalidOperationException)
        {
            return settings;
        }
    }

    private static LumaBrakeSettings WithStoredStartupHardwareBrightness(
        LumaBrakeSettings settings,
        HardwareBrightnessTarget storedStartupHardwareBrightness) =>
        settings with
        {
            StartupRestoration = settings.StartupRestoration with
            {
                HardwareBrightness = storedStartupHardwareBrightness
            }
        };

    private void StartRuntimeHardwareBrightnessCoordinator(LumaBrakeSettings initialSettings)
    {
        _runtimeHardwareBrightnessCoordinator = new RuntimeHardwareBrightnessCoordinator(
            new DdcCiBrightnessWriter(),
            new HardwareBrightnessStateStore(rootDirectory: _settingsStore.RootDirectory));
        _runtimeHardwareBrightnessCoordinator.Seed(initialSettings, DateTimeOffset.Now);
        _runtimeHardwareBrightnessShutdown = new CancellationTokenSource();
        _runtimeHardwareBrightnessLoop = Task.Run(() => RunRuntimeHardwareBrightnessAsync(_runtimeHardwareBrightnessShutdown.Token));
    }

    private async Task RunRuntimeHardwareBrightnessAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(RuntimeHardwareBrightnessPollInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            ApplyRuntimeHardwareBrightness();
        }
    }

    private void ApplyRuntimeHardwareBrightness()
    {
        if (_controller is null || _runtimeHardwareBrightnessCoordinator is null)
        {
            return;
        }

        try
        {
            var result = _runtimeHardwareBrightnessCoordinator.ApplyIfChanged(_controller.Settings, DateTimeOffset.Now);
            if (!result.SoftwareFallbackApplied)
            {
                return;
            }

            _controller.ApplySettings(result.Settings);
            _settingsStore.Save(result.Settings);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void TryRegisterEmergencyHotkey()
    {
        try
        {
            _hotkey = new GlobalHotkey(0x4C42, () => Dispatcher.Invoke(() => _controller?.EmergencyDisable()));
        }
        catch (InvalidOperationException)
        {
            _dashboard?.SetHotkeyStatus("Emergency shortcut unavailable.");
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

    private void ApplyOledProtectionPreset()
    {
        if (_controller is null)
        {
            return;
        }

        var updated = ProtectionTuningPolicy.ApplyOledProtectionPreset(_controller.Settings);
        _controller.ApplySettings(updated, resetDetectorState: true);
        _settingsStore.Save(_controller.Settings);
        _dashboard?.RefreshFromController();
    }

    private void PauseNightColor(TimeSpan duration)
    {
        if (_controller is null)
        {
            return;
        }

        var updated = _controller.Settings with
        {
            NightColor = _controller.Settings.NightColor.Clamp() with
            {
                PausedUntilUtc = DateTimeOffset.UtcNow.Add(duration)
            }
        };
        _controller.ApplySettings(updated);
        _settingsStore.Save(_controller.Settings);
        _dashboard?.RefreshFromController();
    }

    private void ResumeNightColor()
    {
        if (_controller is null)
        {
            return;
        }

        var updated = _controller.Settings with
        {
            NightColor = _controller.Settings.NightColor.Clamp() with
            {
                PausedUntilUtc = null
            }
        };
        _controller.ApplySettings(updated);
        _settingsStore.Save(_controller.Settings);
        _dashboard?.RefreshFromController();
    }

    private TrayNightColorState NightColorTrayState()
    {
        if (_controller is null)
        {
            return new TrayNightColorState(
                Enabled: false,
                UserPaused: false,
                AutoPaused: false,
                ActiveNow: false,
                Summary: "Night Color: Off",
                Detail: "Controller unavailable.");
        }

        var options = _controller.Settings.NightColor.Clamp();
        var now = DateTimeOffset.Now;
        var state = NightColorEngine.Evaluate(options, now);
        var userPaused = options.PausedUntilUtc is { } pausedUntil && pausedUntil.ToUniversalTime() > now.ToUniversalTime();

        if (!options.Enabled)
        {
            return new TrayNightColorState(
                Enabled: false,
                UserPaused: false,
                AutoPaused: false,
                ActiveNow: false,
                Summary: "Night Color: Off",
                Detail: "Disabled.");
        }

        if (userPaused && options.PausedUntilUtc is { } resumeAt)
        {
            return new TrayNightColorState(
                Enabled: true,
                UserPaused: true,
                AutoPaused: false,
                ActiveNow: false,
                Summary: $"Night Color: Paused until {resumeAt.ToLocalTime():HH:mm}",
                Detail: "Use Resume Night Color now to continue.");
        }

        if (state.Reason == "fullscreen-paused")
        {
            return new TrayNightColorState(
                Enabled: true,
                UserPaused: false,
                AutoPaused: true,
                ActiveNow: false,
                Summary: "Night Color: Auto-paused",
                Detail: "Full-screen app is active.");
        }

        if (state.Reason == "application-exception")
        {
            return new TrayNightColorState(
                Enabled: true,
                UserPaused: false,
                AutoPaused: true,
                ActiveNow: false,
                Summary: "Night Color: Auto-paused",
                Detail: "Focused app rule is active.");
        }

        if (state.Enabled)
        {
            return new TrayNightColorState(
                Enabled: true,
                UserPaused: false,
                AutoPaused: false,
                ActiveNow: true,
                Summary: $"Night Color: Active {state.TemperatureKelvin}K",
                Detail: $"Brightness {Math.Round(state.Brightness * 100):0}%.");
        }

        return new TrayNightColorState(
            Enabled: true,
            UserPaused: false,
            AutoPaused: false,
            ActiveNow: false,
            Summary: "Night Color: Waiting",
            Detail: "Schedule is ready.");
    }

    private void ExportDiagnostics()
    {
        ShowDashboard();
        _dashboard?.ExportDiagnostics();
    }

    private void ExitApplication()
    {
        _dashboard?.AllowApplicationClose();
        Shutdown();
    }
}
