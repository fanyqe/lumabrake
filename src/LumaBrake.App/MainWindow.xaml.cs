using System.Globalization;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using LumaBrake.Core;
using LumaBrake.Diagnostics;
using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Color;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Monitors;
using LumaBrake.Domain.Profiles;
using LumaBrake.Domain.Rules;
using LumaBrake.Persistence;
using LumaBrake.Platform.Windows;

namespace LumaBrake.App;

public partial class MainWindow : Window
{
    private static readonly TimeSpan SnapshotRenderInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan NightColorPreviewFrameInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan NightColorPreviewTimeStep = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BrightnessHistoryMinimumSampleInterval = TimeSpan.FromMilliseconds(250);
    private static readonly string[] ClockInputFormats = ["H:mm", "HH:mm", "H.mm", "HH.mm"];
    private const int BrightnessHistoryCapacity = 120;
    private const int AutomaticDayTemperatureKelvin = 6500;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeFallback = 19;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;
    private const int DwmRoundCorners = 2;
    private const int DarkChromeColorRef = 0x000F0A07;
    private const int DarkBorderColorRef = 0x00443228;
    private const int LightTextColorRef = 0x00FFF8F4;
    private const string GitHubRepositoryUrl = "https://github.com/fanyqe/lumabrake";

    private readonly LumaBrakeController _controller;
    private readonly SettingsStore _settingsStore;
    private readonly DashboardDiagnosticsExporter _diagnosticsExporter;
    private readonly WindowsStartupRegistration _startupRegistration;
    private readonly Queue<BrightnessHistorySample> _brightnessHistory = new();
    private readonly object _snapshotRenderGate = new();
    private readonly DispatcherTimer _snapshotRenderTimer;
    private readonly DispatcherTimer _nightColorPreviewTimer;
    private bool _allowApplicationClose;
    private bool _updatingControls;
    private bool _hasUnsavedChanges;
    private bool _snapshotRenderQueued;
    private bool _nightColorPreviewActive;
    private DateTimeOffset _lastSnapshotRenderedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nightColorPreviewBaseTime = DateTimeOffset.MinValue;
    private DateTimeOffset _nightColorPreviewCurrentTime = DateTimeOffset.MinValue;
    private PipelineSnapshot? _pendingSnapshot;
    private DateTimeOffset _lastBrightnessHistorySampleAt = DateTimeOffset.MinValue;
    private string? _selectedProfileId;
    private string? _selectedApplicationRuleId;
    private string? _selectedMonitorManualDimmingId;

    public MainWindow(
        LumaBrakeController controller,
        SettingsStore settingsStore,
        DashboardDiagnosticsExporter? diagnosticsExporter = null,
        WindowsStartupRegistration? startupRegistration = null)
    {
        _controller = controller;
        _settingsStore = settingsStore;
        _diagnosticsExporter = diagnosticsExporter ?? new DashboardDiagnosticsExporter();
        _startupRegistration = startupRegistration ?? new WindowsStartupRegistration();
        InitializeComponent();
        _snapshotRenderTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = SnapshotRenderInterval
        };
        _snapshotRenderTimer.Tick += SnapshotRenderTimerOnTick;
        _nightColorPreviewTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
        {
            Interval = NightColorPreviewFrameInterval
        };
        _nightColorPreviewTimer.Tick += NightColorPreviewTimerOnTick;
        NightColorPreviewTimeline.SizeChanged += (_, _) =>
        {
            if (!_nightColorPreviewActive)
            {
                RenderNightColorSummary();
            }
        };

        RenderSettingsControls();
        RenderStartupStatus();
        _controller.SnapshotUpdated += ControllerOnSnapshotUpdated;
        RenderSnapshot(_controller.Snapshot());
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyDarkWindowFrame();
    }

    public void SetHotkeyStatus(string status)
    {
        HotkeyText.Text = status;
    }

    public void AllowApplicationClose()
    {
        _allowApplicationClose = true;
    }

    public void ExportDiagnostics()
    {
        try
        {
            var snapshot = _controller.Snapshot();
            var monitorError = snapshot.MonitorCount == 0 ? snapshot.MonitorStatus : null;
            var exportedPath = _diagnosticsExporter.Export(
                _controller.Settings,
                _controller.Monitors(),
                monitorError,
                snapshot);

            FooterText.Text = "Diagnostics package saved.";
            HotkeyText.Text = exportedPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            FooterText.Text = "Diagnostics export failed.";
            HotkeyText.Text = ex.Message;
        }
    }

    public void RefreshFromController()
    {
        RenderSettingsControls();
        RenderSnapshot(_controller.Snapshot());
    }

    protected override void OnClosed(EventArgs e)
    {
        _controller.SnapshotUpdated -= ControllerOnSnapshotUpdated;
        _snapshotRenderTimer.Stop();
        _controller.StopNightColorPreview();
        _nightColorPreviewTimer.Stop();
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

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowMaximize();
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleWindowMaximize();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void NavigationButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.RadioButton { Tag: string tag } ||
            !int.TryParse(tag, CultureInfo.InvariantCulture, out var tabIndex))
        {
            return;
        }

        if (tabIndex >= 0 && tabIndex < WorkspaceTabs.Items.Count)
        {
            WorkspaceTabs.SelectedIndex = tabIndex;
        }
    }

    private void WorkspaceTabs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, WorkspaceTabs))
        {
            return;
        }

        SetNavigationSelection(WorkspaceTabs.SelectedIndex);
    }

    private void ToggleWindowMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void SetNavigationSelection(int tabIndex)
    {
        if (OverviewNavButton is null ||
            MonitorsNavButton is null ||
            NightColorNavButton is null ||
            RulesNavButton is null ||
            DiagnosticsNavButton is null ||
            SettingsNavButton is null)
        {
            return;
        }

        OverviewNavButton.IsChecked = tabIndex == 0;
        MonitorsNavButton.IsChecked = tabIndex == 1;
        NightColorNavButton.IsChecked = tabIndex == 2;
        RulesNavButton.IsChecked = tabIndex == 3;
        DiagnosticsNavButton.IsChecked = tabIndex == 4;
        SettingsNavButton.IsChecked = tabIndex == 5;
    }

    private void OpenSettingsSectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } &&
            int.TryParse(tag, CultureInfo.InvariantCulture, out var tabIndex) &&
            tabIndex >= 0 &&
            tabIndex < WorkspaceTabs.Items.Count)
        {
            WorkspaceTabs.SelectedIndex = tabIndex;
        }
    }

    private void ControllerOnSnapshotUpdated(object? sender, PipelineSnapshot snapshot)
    {
        lock (_snapshotRenderGate)
        {
            _pendingSnapshot = snapshot;
            if (_snapshotRenderQueued)
            {
                return;
            }

            _snapshotRenderQueued = true;
        }

        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            lock (_snapshotRenderGate)
            {
                _snapshotRenderQueued = false;
            }

            return;
        }

        Dispatcher.BeginInvoke(new Action(SchedulePendingSnapshotRender), DispatcherPriority.Background);
    }

    private void SchedulePendingSnapshotRender()
    {
        var now = DateTimeOffset.UtcNow;
        var nextRenderAt = _lastSnapshotRenderedAt + SnapshotRenderInterval;
        if (_lastSnapshotRenderedAt != DateTimeOffset.MinValue && now < nextRenderAt)
        {
            _snapshotRenderTimer.Interval = nextRenderAt - now;
            _snapshotRenderTimer.Start();
            return;
        }

        RenderPendingSnapshot();
    }

    private void SnapshotRenderTimerOnTick(object? sender, EventArgs e)
    {
        _snapshotRenderTimer.Stop();
        RenderPendingSnapshot();
    }

    private void RenderPendingSnapshot()
    {
        PipelineSnapshot? snapshot;
        lock (_snapshotRenderGate)
        {
            snapshot = _pendingSnapshot;
            _pendingSnapshot = null;
            _snapshotRenderQueued = false;
            _lastSnapshotRenderedAt = DateTimeOffset.UtcNow;
        }

        if (snapshot is not null)
        {
            RenderSnapshot(snapshot);
        }
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
        _hasUnsavedChanges = false;
        FooterText.Text = "Settings saved.";
    }

    private void ExportDiagnosticsButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExportDiagnostics();
    }

    private void OpenSourceButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(GitHubRepositoryUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            FooterText.Text = "GitHub link could not be opened.";
            HotkeyText.Text = GitHubRepositoryUrl;
        }
    }

    private void EnableStartupButton_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateStartupRegistration(enable: true);
    }

    private void DisableStartupButton_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateStartupRegistration(enable: false);
    }

    private void SaveProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var id = ProfileIdTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            FooterText.Text = "Profile ID is required.";
            return;
        }

        var name = ProfileNameTextBox.Text.Trim();
        var current = _controller.Settings;
        var profile = ProfileSettingsPolicy.Capture(id, string.IsNullOrWhiteSpace(name) ? id : name, current);
        _controller.ApplySettings(current with { Profiles = current.Profiles.Upsert(profile) });
        _settingsStore.Save(_controller.Settings);
        _selectedProfileId = profile.Id;
        _hasUnsavedChanges = false;
        RenderProfiles();
        FooterText.Text = "Profile saved.";
    }

    private void ApplyProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var id = SelectedProfileId();
        var profile = _controller.Settings.Profiles.Find(id);
        if (profile is null)
        {
            FooterText.Text = "Profile not found.";
            return;
        }

        _selectedProfileId = profile.Id;
        _controller.ApplySettings(ProfileSettingsPolicy.Apply(_controller.Settings, profile));
        _settingsStore.Save(_controller.Settings);
        _hasUnsavedChanges = false;
        RenderSettingsControls();
        RenderSnapshot(_controller.Snapshot());
        FooterText.Text = "Profile applied.";
    }

    private void RemoveProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var id = SelectedProfileId();
        if (_controller.Settings.Profiles.Find(id) is null)
        {
            FooterText.Text = "Profile not found.";
            return;
        }

        var updated = _controller.Settings with { Profiles = _controller.Settings.Profiles.Remove(id) };
        _controller.ApplySettings(updated);
        _settingsStore.Save(_controller.Settings);
        _selectedProfileId = null;
        _hasUnsavedChanges = false;
        RenderProfiles();
        FooterText.Text = "Profile removed.";
    }

    private void SaveApplicationRuleButton_OnClick(object sender, RoutedEventArgs e)
    {
        var id = ApplicationRuleIdTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            FooterText.Text = "Rule ID is required.";
            return;
        }

        var processName = Normalize(ApplicationRuleProcessTextBox.Text);
        var windowTitleContains = Normalize(ApplicationRuleTitleTextBox.Text);
        if (processName is null && windowTitleContains is null)
        {
            FooterText.Text = "Process or window title is required.";
            return;
        }

        var name = Normalize(ApplicationRuleNameTextBox.Text) ?? id;
        var rule = new ApplicationRule
        {
            Id = id,
            Name = name,
            Enabled = ApplicationRuleEnabledCheckBox.IsChecked == true,
            ProcessName = processName,
            WindowTitleContains = windowTitleContains,
            ExcludeAutomaticEffects = true,
            ExcludeNightColor = ApplicationRuleNightColorCheckBox.IsChecked == true
        }.Clamp();

        var current = _controller.Settings;
        _controller.ApplySettings(current with
        {
            ApplicationRules = current.ApplicationRules.Upsert(rule) with
            {
                Enabled = ApplicationRulesCheckBox.IsChecked == true
            }
        });
        _settingsStore.Save(_controller.Settings);
        _selectedApplicationRuleId = rule.Id;
        _hasUnsavedChanges = false;
        RenderApplicationRules();
        FooterText.Text = "Application rule saved.";
    }

    private void RemoveApplicationRuleButton_OnClick(object sender, RoutedEventArgs e)
    {
        var id = SelectedApplicationRuleId();
        if (_controller.Settings.ApplicationRules.Find(id) is null)
        {
            FooterText.Text = "Application rule not found.";
            return;
        }

        var updated = _controller.Settings with
        {
            ApplicationRules = _controller.Settings.ApplicationRules.Remove(id)
        };
        _controller.ApplySettings(updated);
        _settingsStore.Save(_controller.Settings);
        _selectedApplicationRuleId = null;
        _hasUnsavedChanges = false;
        RenderApplicationRules();
        FooterText.Text = "Application rule removed.";
    }

    private void ClearApplicationRulesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var updated = _controller.Settings with
        {
            ApplicationRules = _controller.Settings.ApplicationRules with
            {
                Enabled = false,
                Entries = []
            }
        };
        _controller.ApplySettings(updated);
        _settingsStore.Save(_controller.Settings);
        _selectedApplicationRuleId = null;
        _hasUnsavedChanges = false;
        RenderApplicationRules();
        FooterText.Text = "Application rules cleared.";
    }

    private void ApplicationRulesCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingControls || !IsInitialized)
        {
            return;
        }

        var current = _controller.Settings;
        _controller.ApplySettings(current with
        {
            ApplicationRules = current.ApplicationRules.Clamp() with
            {
                Enabled = ApplicationRulesCheckBox.IsChecked == true
            }
        });
        MarkSettingsChanged();
    }

    private void ApplicationRuleComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingControls)
        {
            return;
        }

        if (ApplicationRuleComboBox.SelectedItem is not ApplicationRuleListItem item)
        {
            UpdateApplicationRuleButtonsAvailability();
            return;
        }

        _selectedApplicationRuleId = item.Id;
        RenderSelectedApplicationRuleFields(item.Id);
        UpdateApplicationRuleButtonsAvailability();
    }

    private void ApplicationRuleEditor_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_updatingControls)
        {
            UpdateApplicationRuleButtonsAvailability();
        }
    }

    private void ProfileComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingControls)
        {
            return;
        }

        if (ProfileComboBox.SelectedItem is not ProfileListItem item)
        {
            UpdateProfileButtonsAvailability();
            return;
        }

        _selectedProfileId = item.Id;
        RenderSelectedProfileFields(item.Id);
        UpdateProfileButtonsAvailability();
    }

    private void ProfileTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updatingControls)
        {
            UpdateProfileButtonsAvailability();
        }
    }

    private void RefreshMonitorsButton_OnClick(object sender, RoutedEventArgs e)
    {
        _controller.RefreshMonitors();
        RenderSnapshot(_controller.Snapshot());
        RenderMonitorManualDimmingControls();
    }

    private void MaximumOpacitySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _updatingControls)
        {
            return;
        }

        _controller.SetUserMaximumOpacity(e.NewValue);
        MaximumOpacityText.Text = FormatPercent(e.NewValue);
        MarkSettingsChanged();
    }

    private void ManualDimmingSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _updatingControls)
        {
            return;
        }

        _controller.SetManualDimmingOpacity(e.NewValue);
        ManualDimmingText.Text = FormatPercent(e.NewValue);
        RenderSelectedMonitorManualDimmingValue();
        MarkSettingsChanged();
    }

    private void MonitorDimmingComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || _updatingControls)
        {
            return;
        }

        _selectedMonitorManualDimmingId = SelectedMonitorManualDimmingId();
        RenderSelectedMonitorManualDimmingValue();
    }

    private void MonitorManualDimmingSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _updatingControls)
        {
            return;
        }

        var monitor = SelectedMonitorManualDimmingMonitor();
        if (monitor is null)
        {
            return;
        }

        var existing = _controller.Settings.MonitorManualDimming.FindForMonitor(monitor);
        _controller.SetMonitorManualDimmingOpacity(existing?.MonitorId ?? monitor.StableId, e.NewValue);
        MonitorManualDimmingText.Text = FormatPercent(e.NewValue);
        ClearMonitorDimmingButton.IsEnabled = true;
        MarkSettingsChanged();
    }

    private void ClearMonitorDimmingButton_OnClick(object sender, RoutedEventArgs e)
    {
        var monitor = SelectedMonitorManualDimmingMonitor();
        if (monitor is null)
        {
            FooterText.Text = "Select a monitor.";
            return;
        }

        var existing = _controller.Settings.MonitorManualDimming.FindForMonitor(monitor);
        if (existing is null)
        {
            return;
        }

        _controller.ClearMonitorManualDimming(existing.MonitorId);
        RenderSelectedMonitorManualDimmingValue();
        MarkSettingsChanged();
    }

    private void SettingCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, StaticShadeCheckBox) &&
            StaticShadeCheckBox.IsChecked == true &&
            StaticShadePresetComboBox.SelectedIndex == StaticShadePresetIndex(StaticShadePreset.Disabled))
        {
            _updatingControls = true;
            StaticShadePresetComboBox.SelectedIndex = StaticShadePresetIndex(StaticShadePreset.OledBalanced);
            _updatingControls = false;
        }

        ApplyDashboardSettings(resetDetectorState: true);
    }

    private void ProtectionTuningComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyDashboardSettings(resetDetectorState: true);
    }

    private void ProtectionPresetComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingControls || !IsInitialized)
        {
            return;
        }

        var preset = ProtectionPresetFromIndex(sender is System.Windows.Controls.ComboBox comboBox ? comboBox.SelectedIndex : -1);
        if (preset == ProtectionPreset.Custom)
        {
            RenderProtectionPresetState(_controller.Settings);
            FooterText.Text = "Custom uses the detailed tuning below.";
            return;
        }

        var updated = ProtectionTuningPolicy.ApplyProtectionPreset(_controller.Settings, preset);
        _controller.ApplySettings(updated, resetDetectorState: true);
        _settingsStore.Save(_controller.Settings);
        _hasUnsavedChanges = false;
        RenderSettingsControls();
        RenderSnapshot(_controller.Snapshot());
        FooterText.Text = $"{ProtectionPresetDisplayName(preset)} protection preset applied.";
    }

    private void FocusOpacitySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        FocusOpacityText.Text = FormatPercent(e.NewValue);
        ApplyDashboardSettings(resetDetectorState: false);
    }

    private void TintOpacitySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        TintOpacityText.Text = FormatPercent(e.NewValue);
        ApplyDashboardSettings(resetDetectorState: false);
    }

    private void TintModeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyDashboardSettings(resetDetectorState: false);
    }

    private void NightColorControl_OnChanged(object sender, RoutedEventArgs e)
    {
        ApplyDashboardSettings(resetDetectorState: false);
    }

    private void NightColorControl_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyDashboardSettings(resetDetectorState: false);
    }

    private void NightColorTemperatureSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (NightColorTemperatureTextBox is null)
        {
            return;
        }

        SetTextBoxText(NightColorTemperatureTextBox, FormatKelvin(e.NewValue));
        ApplyDashboardSettings(resetDetectorState: false);
    }

    private void NightColorDayTemperatureSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (NightColorDayTemperatureTextBox is null)
        {
            return;
        }

        SetTextBoxText(NightColorDayTemperatureTextBox, FormatKelvin(e.NewValue));
        ApplyDashboardSettings(resetDetectorState: false);
    }

    private void NightColorNightTemperatureSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (NightColorNightTemperatureTextBox is null)
        {
            return;
        }

        SetTextBoxText(NightColorNightTemperatureTextBox, FormatKelvin(e.NewValue));
        ApplyDashboardSettings(resetDetectorState: false);
    }

    private void NightColorBrightnessSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (NightColorBrightnessTextBox is null)
        {
            return;
        }

        SetTextBoxText(NightColorBrightnessTextBox, FormatPercent(e.NewValue));
        ApplyDashboardSettings(resetDetectorState: false);
    }

    private void NightColorTintStrengthSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (NightColorTintStrengthTextBox is null)
        {
            return;
        }

        SetTextBoxText(NightColorTintStrengthTextBox, FormatPercent(e.NewValue));
        ApplyDashboardSettings(resetDetectorState: false);
    }

    private void NightColorTransitionDurationSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (NightColorTransitionDurationTextBox is null)
        {
            return;
        }

        SetTextBoxText(NightColorTransitionDurationTextBox, FormatDurationMinutes(TimeSpan.FromMinutes(e.NewValue)));
        ApplyDashboardSettings(resetDetectorState: false);
    }

    private void NightColorTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        ApplyDashboardSettings(resetDetectorState: false);
        RenderNightColorControls(_controller.Settings.NightColor);
        RenderSettingValueTexts();
    }

    private void NightColorTextBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ApplyDashboardSettings(resetDetectorState: false);
        RenderNightColorControls(_controller.Settings.NightColor);
        RenderSettingValueTexts();
        e.Handled = true;
    }

    private void PauseNightColorButton_OnClick(object sender, RoutedEventArgs e)
    {
        StopNightColorPreview(resetSummary: true);
        var updated = _controller.Settings with
        {
            NightColor = _controller.Settings.NightColor.Clamp() with
            {
                PausedUntilUtc = DateTimeOffset.UtcNow.AddHours(1)
            }
        };
        _controller.ApplySettings(updated);
        _settingsStore.Save(_controller.Settings);
        _hasUnsavedChanges = false;
        RenderSettingsControls();
        FooterText.Text = "Night color paused for 1 hour.";
    }

    private void ResumeNightColorButton_OnClick(object sender, RoutedEventArgs e)
    {
        StopNightColorPreview(resetSummary: true);
        var updated = _controller.Settings with
        {
            NightColor = _controller.Settings.NightColor.Clamp() with
            {
                PausedUntilUtc = null
            }
        };
        _controller.ApplySettings(updated);
        _settingsStore.Save(_controller.Settings);
        _hasUnsavedChanges = false;
        RenderSettingsControls();
        FooterText.Text = "Night color resumed.";
    }

    private void PreviewNightColorButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_nightColorPreviewActive)
        {
            StopNightColorPreview(resetSummary: true);
            FooterText.Text = "Night color preview stopped.";
            return;
        }

        _nightColorPreviewBaseTime = DateTimeOffset.Now;
        _nightColorPreviewCurrentTime = _nightColorPreviewBaseTime;
        _nightColorPreviewActive = true;
        NightColorPreviewButtonIcon.Text = "\uE769";
        NightColorPreviewButtonText.Text = "Previewing";
        FooterText.Text = "Night color preview starting.";
        RenderNightColorPreviewFrame(_nightColorPreviewCurrentTime);
        _nightColorPreviewTimer.Start();
    }

    private void NightColorPreviewTimerOnTick(object? sender, EventArgs e)
    {
        var target = _nightColorPreviewBaseTime.AddDays(1);
        _nightColorPreviewCurrentTime = StepNightColorPreviewTime(
            _nightColorPreviewCurrentTime,
            target,
            NightColorPreviewTimeStep);
        RenderNightColorPreviewFrame(_nightColorPreviewCurrentTime);
        if (_nightColorPreviewCurrentTime >= target)
        {
            StopNightColorPreview(resetSummary: true);
            FooterText.Text = "Night color preview complete.";
        }
    }

    private void RenderNightColorPreviewFrame(DateTimeOffset previewTimestamp)
    {
        RenderNightColorSummary(previewTimestamp);

        var previewOptions = _controller.Settings.NightColor.Clamp() with
        {
            Enabled = true,
            PausedUntilUtc = null
        };
        var previewState = NightColorEngine.Evaluate(previewOptions, previewTimestamp);
        var applied = _controller.PreviewNightColor(previewState);
        FooterText.Text = applied
            ? "Night color preview running on display."
            : "Night color preview shown in dashboard.";
    }

    private static DateTimeOffset StepNightColorPreviewTime(
        DateTimeOffset current,
        DateTimeOffset target,
        TimeSpan step) =>
        current >= target ? target : current.Add(step) > target ? target : current.Add(step);

    private void StopNightColorPreview(bool resetSummary)
    {
        if (!_nightColorPreviewActive && !_nightColorPreviewTimer.IsEnabled)
        {
            return;
        }

        _nightColorPreviewActive = false;
        _nightColorPreviewTimer.Stop();
        _controller.StopNightColorPreview();
        NightColorPreviewButtonIcon.Text = "\uE768";
        NightColorPreviewButtonText.Text = "Preview day";
        if (resetSummary)
        {
            RenderSettingValueTexts();
        }
    }

    private void StaticShadePresetComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingControls || !IsInitialized)
        {
            return;
        }

        _updatingControls = true;
        StaticShadeCheckBox.IsChecked = StaticShadePresetComboBox.SelectedIndex != 5;
        _updatingControls = false;
        ApplyDashboardSettings(resetDetectorState: true);
    }

    private void OledPresetCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingControls || !IsInitialized)
        {
            return;
        }

        var applyOledPreset = OledPresetCheckBox.IsChecked == true;
        var updated = applyOledPreset
            ? ProtectionTuningPolicy.ApplyOledProtectionPreset(_controller.Settings)
            : ProtectionTuningPolicy.ApplyBalancedProtectionPreset(_controller.Settings);
        _controller.ApplySettings(updated, resetDetectorState: true);
        _settingsStore.Save(_controller.Settings);
        _hasUnsavedChanges = false;
        RenderSettingsControls();
        RenderSnapshot(_controller.Snapshot());
        FooterText.Text = applyOledPreset
            ? "OLED protection preset applied."
            : "Balanced protection preset applied.";
    }

    private void RenderSnapshot(PipelineSnapshot snapshot)
    {
        StateText.Text = snapshot.IsEnabled ? "Enabled" : "Disabled";
        SettingsProtectionText.Text = StateText.Text;
        ToggleButtonIcon.Text = snapshot.IsEnabled ? "\uE769" : "\uE768";
        ToggleButtonText.Text = snapshot.IsEnabled ? "Disable protection" : "Enable protection";
        ToggleButtonDetailText.Text = snapshot.IsEnabled
            ? "All filters are running"
            : "Click to restore protection";
        ApplyProtectionStateVisual(snapshot.IsEnabled);
        AverageText.Text = snapshot.AverageLuminance.ToString("0.000");
        P95Text.Text = snapshot.Percentile95Luminance.ToString("0.000");
        OpacityText.Text = FormatPercent(snapshot.OverlayOpacity);
        MonitorAverageText.Text = AverageText.Text;
        MonitorP95Text.Text = P95Text.Text;
        MonitorOpacityText.Text = OpacityText.Text;
        var protectionEvents = snapshot.SpikeEvents + snapshot.FlashEvents;
        EventsText.Text = snapshot.RedFlashEvents > 0
            ? $"{protectionEvents:N0} ({snapshot.RedFlashEvents:N0} red)"
            : protectionEvents.ToString("N0");
        ReasonText.Text = FormatReason(snapshot.ActiveReason);
        RegionalMasksText.Text = snapshot.RegionalMaskCount == 0
            ? "0"
            : $"{snapshot.RegionalMaskCount:N0} @ {FormatPercent(snapshot.RegionalMaskMaxOpacity)}";
        if (Math.Abs(ManualDimmingSlider.Value - _controller.Settings.ManualDimmingOpacity) > 0.001 ||
            Math.Abs(MaximumOpacitySlider.Value - _controller.Settings.UserMaximumOpacity) > 0.001)
        {
            _updatingControls = true;
            ManualDimmingSlider.Value = _controller.Settings.ManualDimmingOpacity;
            MaximumOpacitySlider.Value = _controller.Settings.UserMaximumOpacity;
            _updatingControls = false;
        }

        ManualDimmingText.Text = FormatPercent(_controller.Settings.ManualDimmingOpacity);
        MaximumOpacityText.Text = FormatPercent(MaximumOpacitySlider.Value);
        RenderOledPresetState(_controller.Settings);
        RenderMonitorManualDimmingControls();
        var activeMonitor = snapshot.Monitors.FirstOrDefault(monitor =>
                string.Equals(monitor.DeviceName, snapshot.ActiveMonitor, StringComparison.OrdinalIgnoreCase)) ??
            snapshot.Monitors.FirstOrDefault();
        var samplingStatus = FormatSamplingStatus(snapshot);
        MonitorText.Text = snapshot.MonitorCount == 0
            ? $"{snapshot.MonitorStatus}, {samplingStatus}"
            : $"{snapshot.ActiveMonitor}, 160 x 90 BGRA sample, {snapshot.MonitorStatus}, {FormatAdvancedColor(activeMonitor?.AdvancedColor)}, {samplingStatus}";
        MonitorCaptureText.Text = MonitorText.Text;
        AppendBrightnessHistory(snapshot);
        RenderBrightnessHistoryChart();
    }

    private void BrightnessHistoryCanvas_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderBrightnessHistoryChart();
    }

    private void AppendBrightnessHistory(PipelineSnapshot snapshot)
    {
        if (_brightnessHistory.Count > 0 &&
            snapshot.Timestamp - _lastBrightnessHistorySampleAt < BrightnessHistoryMinimumSampleInterval)
        {
            return;
        }

        var protectionEvents = snapshot.SpikeEvents + snapshot.FlashEvents;
        _brightnessHistory.Enqueue(new BrightnessHistorySample(
            snapshot.Timestamp,
            Math.Clamp(snapshot.AverageLuminance, 0, 1),
            Math.Clamp(snapshot.Percentile95Luminance, 0, 1),
            Math.Clamp(snapshot.OverlayOpacity, 0, 1),
            protectionEvents));
        _lastBrightnessHistorySampleAt = snapshot.Timestamp;

        while (_brightnessHistory.Count > BrightnessHistoryCapacity)
        {
            _brightnessHistory.Dequeue();
        }
    }

    private void RenderBrightnessHistoryChart()
    {
        if (ActualBrightnessLine is null ||
            P95BrightnessLine is null ||
            OverlayOpacityLine is null ||
            BrightnessHistoryCanvas is null)
        {
            return;
        }

        var samples = _brightnessHistory.ToArray();
        var width = BrightnessHistoryCanvas.ActualWidth > 1 ? BrightnessHistoryCanvas.ActualWidth : 742;
        var height = BrightnessHistoryCanvas.ActualHeight > 1 ? BrightnessHistoryCanvas.ActualHeight : 176;
        ActualBrightnessLine.Points = ChartPoints(samples, sample => sample.AverageLuminance, width, height);
        P95BrightnessLine.Points = ChartPoints(samples, sample => sample.Percentile95Luminance, width, height);
        OverlayOpacityLine.Points = ChartPoints(samples, sample => sample.OverlayOpacity, width, height);

        BrightnessChartEmptyText.Visibility = samples.Length < 2 ? Visibility.Visible : Visibility.Collapsed;
        BrightnessChartWindowText.Text = samples.Length < 2
            ? "Live"
            : FormatBrightnessChartWindow(samples);
    }

    private static System.Windows.Media.PointCollection ChartPoints(
        IReadOnlyList<BrightnessHistorySample> samples,
        Func<BrightnessHistorySample, double> valueSelector,
        double width,
        double height)
    {
        var points = new System.Windows.Media.PointCollection();
        if (samples.Count == 0)
        {
            return points;
        }

        if (samples.Count == 1)
        {
            var y = ChartY(valueSelector(samples[0]), height);
            points.Add(new System.Windows.Point(0, y));
            points.Add(new System.Windows.Point(width, y));
            return points;
        }

        var lastIndex = samples.Count - 1;
        for (var index = 0; index < samples.Count; index++)
        {
            var x = width * index / lastIndex;
            var y = ChartY(valueSelector(samples[index]), height);
            points.Add(new System.Windows.Point(x, y));
        }

        return points;
    }

    private static double ChartY(double value, double height) =>
        height - (Math.Clamp(value, 0, 1) * height);

    private static string FormatBrightnessChartWindow(IReadOnlyList<BrightnessHistorySample> samples)
    {
        var window = samples[^1].Timestamp - samples[0].Timestamp;
        var seconds = Math.Max(1, Math.Round(window.TotalSeconds));
        var eventDelta = Math.Max(0, samples[^1].ProtectionEvents - samples[0].ProtectionEvents);
        return eventDelta > 0
            ? $"{seconds.ToString("0", CultureInfo.InvariantCulture)}s +{eventDelta.ToString("N0", CultureInfo.CurrentCulture)}"
            : $"{seconds.ToString("0", CultureInfo.InvariantCulture)}s live";
    }

    private void ApplyProtectionStateVisual(bool isEnabled)
    {
        var stateBrush = ResourceBrush(isEnabled ? "SuccessBrush" : "DangerBrush");
        ProtectionStateBadge.Background = ResourceBrush(isEnabled ? "SuccessSoftBrush" : "DangerSoftBrush");
        ProtectionStateBadge.BorderBrush = stateBrush;
        ProtectionStateIcon.Foreground = stateBrush;
        ProtectionStateIcon.Text = isEnabled ? "\uE73E" : "\uE711";
        StateText.Foreground = stateBrush;
        SettingsProtectionText.Foreground = stateBrush;
    }

    private void RenderSettingsControls()
    {
        _updatingControls = true;
        try
        {
            var settings = _controller.Settings;
            MaximumOpacitySlider.Value = settings.UserMaximumOpacity;
            ManualDimmingSlider.Value = settings.ManualDimmingOpacity;
            var tuning = settings.ProtectionTuning.Clamp();
            ProtectionStrengthComboBox.SelectedIndex = (int)tuning.Strength;
            ProtectionResponseComboBox.SelectedIndex = (int)tuning.Response;
            LocalProtectionComboBox.SelectedIndex = (int)tuning.LocalProtection;
            LocalMaskFitComboBox.SelectedIndex = (int)tuning.LocalMaskFit;
            WhitePageProtectionComboBox.SelectedIndex = (int)tuning.WhitePageProtection;
            SpikeGuardCheckBox.IsChecked = settings.SpikeGuard.Enabled;
            FlashGuardCheckBox.IsChecked = settings.FlashGuard.Enabled;
            RedFlashCheckBox.IsChecked = settings.FlashGuard.RedFlashDetectionEnabled;
            AdaptiveContentCheckBox.IsChecked = settings.AdaptiveContent.Enabled;
            InactiveMonitorCheckBox.IsChecked = settings.InactiveMonitor.Enabled;
            StaticShadeCheckBox.IsChecked = settings.StaticShade.Enabled && tuning.StaticShadeProfile != StaticShadePreset.Disabled;
            StaticShadePresetComboBox.SelectedIndex = StaticShadePresetIndex(tuning.StaticShadeProfile);
            RenderProtectionPresetState(settings);
            RenderOledPresetState(settings);
            var focus = settings.FocusDimming.Clamp();
            FocusDimmingCheckBox.IsChecked = focus.Enabled;
            FocusOpacitySlider.Value = focus.Opacity;
            TintCheckBox.IsChecked = settings.Tint.Enabled;
            TintModeComboBox.SelectedIndex = TintModeIndex(settings.Tint);
            TintOpacitySlider.Value = settings.Tint.Opacity;
            RenderNightColorControls(settings.NightColor);
            RenderSettingValueTexts();
            UpdateSettingControlAvailability();
            RenderMonitorManualDimmingControls();
            RenderProfiles();
            RenderApplicationRules();
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private void RenderStartupStatus()
    {
        try
        {
            var appPath = StartupExecutablePath();
            if (appPath is null)
            {
                StartupStatusText.Text = "Startup controls are unavailable for this launch.";
                EnableStartupButton.IsEnabled = false;
                DisableStartupButton.IsEnabled = false;
                return;
            }

            var status = _startupRegistration.GetStatus(appPath);
            StartupStatusText.Text = FormatStartupStatus(status);
            EnableStartupButton.IsEnabled = !status.IsCurrent;
            DisableStartupButton.IsEnabled = status.IsRegistered;
        }
        catch (Exception ex) when (IsStartupRegistrationException(ex))
        {
            StartupStatusText.Text = "Startup status is unavailable.";
            HotkeyText.Text = ex.Message;
            EnableStartupButton.IsEnabled = false;
            DisableStartupButton.IsEnabled = false;
        }
    }

    private void UpdateStartupRegistration(bool enable)
    {
        try
        {
            if (enable)
            {
                var appPath = StartupExecutablePath();
                if (appPath is null)
                {
                    FooterText.Text = "Windows startup cannot be enabled from this launch.";
                    RenderStartupStatus();
                    return;
                }

                _startupRegistration.Enable(appPath);
                FooterText.Text = "Windows startup enabled.";
            }
            else
            {
                _startupRegistration.Disable();
                FooterText.Text = "Windows startup disabled.";
            }
        }
        catch (Exception ex) when (IsStartupRegistrationException(ex))
        {
            FooterText.Text = "Windows startup update failed.";
            StartupStatusText.Text = ex.Message;
            return;
        }

        RenderStartupStatus();
    }

    private void ApplyDashboardSettings(bool resetDetectorState)
    {
        if (_updatingControls || !IsInitialized)
        {
            return;
        }

        var current = _controller.Settings;
        var tuning = ProtectionTuningFromControls(current.ProtectionTuning);
        var updated = current with
        {
            ProtectionTuning = tuning,
            SpikeGuard = current.SpikeGuard with { Enabled = SpikeGuardCheckBox.IsChecked == true },
            FlashGuard = current.FlashGuard with
            {
                Enabled = FlashGuardCheckBox.IsChecked == true,
                RedFlashDetectionEnabled = RedFlashCheckBox.IsChecked == true
            },
            StaticShade = StaticShadeFromControls(current.StaticShade, tuning),
            AdaptiveContent = current.AdaptiveContent with { Enabled = AdaptiveContentCheckBox.IsChecked == true },
            InactiveMonitor = current.InactiveMonitor with { Enabled = InactiveMonitorCheckBox.IsChecked == true },
            FocusDimming = current.FocusDimming.Clamp() with
            {
                Enabled = FocusDimmingCheckBox.IsChecked == true && FocusOpacitySlider.Value > 0,
                Opacity = Math.Clamp(FocusOpacitySlider.Value, 0, 0.60)
            },
            Tint = TintFromControls(),
            NightColor = NightColorFromControls(current.NightColor)
        };

        updated = ProtectionTuningPolicy.Apply(updated);
        _controller.ApplySettings(updated, resetDetectorState);
        RenderTunedSettingsControls();
        RenderSettingValueTexts();
        UpdateSettingControlAvailability();
        MarkSettingsChanged();
    }

    private ProtectionTuningOptions ProtectionTuningFromControls(ProtectionTuningOptions current) =>
        current.Clamp() with
        {
            Strength = ProtectionStrengthFromIndex(ProtectionStrengthComboBox.SelectedIndex),
            Response = ProtectionResponseFromIndex(ProtectionResponseComboBox.SelectedIndex),
            LocalProtection = LocalProtectionFromIndex(LocalProtectionComboBox.SelectedIndex),
            LocalMaskFit = LocalMaskFitFromIndex(LocalMaskFitComboBox.SelectedIndex),
            WhitePageProtection = WhitePageProtectionFromIndex(WhitePageProtectionComboBox.SelectedIndex),
            StaticShadeProfile = StaticShadePresetFromIndex(StaticShadePresetComboBox.SelectedIndex)
        };

    private StaticShadeOptions StaticShadeFromControls(StaticShadeOptions current, ProtectionTuningOptions tuning)
    {
        var enabled = StaticShadeCheckBox.IsChecked == true && tuning.StaticShadeProfile != StaticShadePreset.Disabled;

        return current with { Enabled = enabled };
    }

    private void RenderOledPresetState(LumaBrakeSettings settings)
    {
        if (OledPresetCheckBox is null)
        {
            return;
        }

        var active = ProtectionTuningPolicy.IsOledProtectionPreset(settings);
        var wasUpdating = _updatingControls;
        _updatingControls = true;
        try
        {
            OledPresetCheckBox.IsChecked = active;
            OledPresetStateText.Text = active
                ? "Static + idle care"
                : "Apply OLED preset";
            OledPresetCard.Background = ResourceBrush(active ? "OledPresetActiveBrush" : "SurfaceBrush");
            OledPresetCard.BorderBrush = ResourceBrush(active ? "OledPresetActiveBorderBrush" : "RuleBrush");
            OledPresetIcon.Foreground = ResourceBrush(active ? "AmberBrush" : "AccentHoverBrush");
            OledPresetGlowBar.Opacity = active ? 1 : 0;
        }
        finally
        {
            _updatingControls = wasUpdating;
        }
    }

    private void RenderProtectionPresetState(LumaBrakeSettings settings)
    {
        if (OverviewProtectionPresetComboBox is null || MonitorProtectionPresetComboBox is null)
        {
            return;
        }

        var preset = ProtectionTuningPolicy.DetectProtectionPreset(settings);
        var presetIndex = ProtectionPresetIndex(preset);
        var visual = ProtectionPresetVisual(preset);
        var wasUpdating = _updatingControls;
        _updatingControls = true;
        try
        {
            OverviewProtectionPresetComboBox.SelectedIndex = presetIndex;
            MonitorProtectionPresetComboBox.SelectedIndex = presetIndex;
            OverviewProtectionPresetDetailText.Text = visual.Detail;
            MonitorProtectionPresetDetailText.Text = visual.Detail;
            OverviewProtectionPresetIcon.Text = visual.Icon;
            MonitorProtectionPresetIcon.Text = visual.Icon;
            OverviewProtectionPresetBadge.Background = ResourceBrush(visual.SoftBrush);
            MonitorProtectionPresetBadge.Background = ResourceBrush(visual.SoftBrush);
            OverviewProtectionPresetBadge.BorderBrush = ResourceBrush(visual.AccentBrush);
            MonitorProtectionPresetBadge.BorderBrush = ResourceBrush(visual.AccentBrush);
            OverviewProtectionPresetIcon.Foreground = ResourceBrush(visual.AccentBrush);
            MonitorProtectionPresetIcon.Foreground = ResourceBrush(visual.AccentBrush);
        }
        finally
        {
            _updatingControls = wasUpdating;
        }
    }

    private void RenderTunedSettingsControls()
    {
        var wasUpdating = _updatingControls;
        _updatingControls = true;
        try
        {
            var settings = _controller.Settings;
            var tuning = settings.ProtectionTuning.Clamp();
            MaximumOpacitySlider.Value = settings.UserMaximumOpacity;
            ProtectionStrengthComboBox.SelectedIndex = (int)tuning.Strength;
            ProtectionResponseComboBox.SelectedIndex = (int)tuning.Response;
            LocalProtectionComboBox.SelectedIndex = (int)tuning.LocalProtection;
            LocalMaskFitComboBox.SelectedIndex = (int)tuning.LocalMaskFit;
            WhitePageProtectionComboBox.SelectedIndex = (int)tuning.WhitePageProtection;
            StaticShadeCheckBox.IsChecked = settings.StaticShade.Enabled && tuning.StaticShadeProfile != StaticShadePreset.Disabled;
            StaticShadePresetComboBox.SelectedIndex = StaticShadePresetIndex(tuning.StaticShadeProfile);
            RenderProtectionPresetState(settings);
            RenderOledPresetState(settings);
        }
        finally
        {
            _updatingControls = wasUpdating;
        }
    }

    private void RenderNightColorControls(NightColorOptions options)
    {
        var nightColor = options.Clamp();
        var wasUpdating = _updatingControls;
        _updatingControls = true;
        try
        {
            NightColorCheckBox.IsChecked = nightColor.Enabled;
            NightColorModeComboBox.SelectedIndex = NightColorModeIndex(nightColor.ScheduleMode);
            NightColorTemperatureSlider.Value = nightColor.ManualTemperatureKelvin;
            NightColorDayTemperatureSlider.Value = nightColor.DayTemperatureKelvin;
            NightColorNightTemperatureSlider.Value = nightColor.NightTemperatureKelvin;
            NightColorBrightnessSlider.Value = nightColor.Brightness;
            NightColorTintStrengthSlider.Value = nightColor.TintStrength;
            NightColorTransitionDurationSlider.Value = nightColor.TransitionDuration.TotalMinutes;
            SetTextBoxText(NightColorTemperatureTextBox, FormatKelvin(nightColor.ManualTemperatureKelvin));
            SetTextBoxText(NightColorDayTemperatureTextBox, FormatKelvin(nightColor.DayTemperatureKelvin));
            SetTextBoxText(NightColorNightTemperatureTextBox, FormatKelvin(nightColor.NightTemperatureKelvin));
            SetTextBoxText(NightColorBrightnessTextBox, FormatPercent(nightColor.Brightness));
            SetTextBoxText(NightColorTintStrengthTextBox, FormatPercent(nightColor.TintStrength));
            SetTextBoxText(NightColorTransitionDurationTextBox, FormatDurationMinutes(nightColor.TransitionDuration));
            SetTextBoxText(NightColorCustomStartTextBox, FormatMinuteOfDay(nightColor.CustomStartMinuteOfDay));
            SetTextBoxText(NightColorCustomEndTextBox, FormatMinuteOfDay(nightColor.CustomEndMinuteOfDay));
            SetTextBoxText(NightColorLocationTextBox, DisplayNightColorLocation(nightColor));
            SetTextBoxText(NightColorLatitudeTextBox, FormatCoordinate(nightColor.Latitude));
            SetTextBoxText(NightColorLongitudeTextBox, FormatCoordinate(nightColor.Longitude));
            NightColorComfortComboBox.SelectedIndex = NightColorComfortIndex(nightColor);
            NightColorTransitionComboBox.SelectedIndex = nightColor.TransitionMode == NightColorTransitionMode.Instant ? 1 : 0;
            NightColorPauseFullScreenCheckBox.IsChecked = nightColor.PauseInFullScreenApplications;
        }
        finally
        {
            _updatingControls = wasUpdating;
        }
    }

    private TintOptions TintFromControls()
    {
        var opacity = Math.Clamp(TintOpacitySlider.Value, 0, 0.60);
        var enabled = TintCheckBox.IsChecked == true && opacity > 0;
        return TintModeComboBox.SelectedIndex == 1
            ? new TintOptions { Enabled = enabled, Red = 255, Green = 244, Blue = 230, Opacity = opacity }
            : new TintOptions { Enabled = enabled, Red = 255, Green = 214, Blue = 170, Opacity = opacity };
    }

    private NightColorOptions NightColorFromControls(NightColorOptions current)
    {
        var clamped = current.Clamp();
        var mode = NightColorModeFromIndex(NightColorModeComboBox.SelectedIndex);
        var comfort = NightColorComfortFromIndex(NightColorComfortComboBox.SelectedIndex);
        var transitionDuration = TryParseTransitionDuration(NightColorTransitionDurationTextBox.Text, out var parsedTransitionDuration)
            ? parsedTransitionDuration
            : TimeSpan.FromMinutes(Math.Clamp(NightColorTransitionDurationSlider.Value, 0, 360));
        var customStart = TryParseClockMinute(NightColorCustomStartTextBox.Text, out var parsedCustomStart)
            ? parsedCustomStart
            : clamped.CustomStartMinuteOfDay;
        var customEnd = TryParseClockMinute(NightColorCustomEndTextBox.Text, out var parsedCustomEnd)
            ? parsedCustomEnd
            : clamped.CustomEndMinuteOfDay;
        var locationName = NightColorLocationCatalog.NormalizeLocationName(NightColorLocationTextBox.Text);
        var latitude = clamped.Latitude;
        var longitude = clamped.Longitude;
        if (NightColorLocationCatalog.TryResolve(locationName, out var location))
        {
            locationName = location.Name;
            latitude = location.Latitude;
            longitude = location.Longitude;
        }

        return (clamped with
        {
            Enabled = NightColorCheckBox.IsChecked == true,
            ScheduleMode = mode,
            ManualTemperatureKelvin = comfort.NightTemperatureKelvin,
            DayTemperatureKelvin = AutomaticDayTemperatureKelvin,
            NightTemperatureKelvin = comfort.NightTemperatureKelvin,
            Brightness = comfort.Brightness,
            TintStrength = comfort.TintStrength,
            CustomStartMinuteOfDay = customStart,
            CustomEndMinuteOfDay = customEnd,
            LocationName = locationName,
            Latitude = latitude,
            Longitude = longitude,
            PauseInFullScreenApplications = NightColorPauseFullScreenCheckBox.IsChecked == true,
            TransitionDuration = transitionDuration,
            TransitionMode = NightColorTransitionComboBox.SelectedIndex == 1
                ? NightColorTransitionMode.Instant
                : NightColorTransitionMode.Smooth
        }).Clamp();
    }

    private void RenderSettingValueTexts()
    {
        ManualDimmingText.Text = FormatPercent(ManualDimmingSlider.Value);
        MaximumOpacityText.Text = FormatPercent(MaximumOpacitySlider.Value);
        FocusOpacityText.Text = FormatPercent(FocusOpacitySlider.Value);
        TintOpacityText.Text = FormatPercent(TintOpacitySlider.Value);
        SetTextBoxText(NightColorTemperatureTextBox, FormatKelvin(NightColorTemperatureSlider.Value));
        SetTextBoxText(NightColorDayTemperatureTextBox, FormatKelvin(NightColorDayTemperatureSlider.Value));
        SetTextBoxText(NightColorNightTemperatureTextBox, FormatKelvin(NightColorNightTemperatureSlider.Value));
        SetTextBoxText(NightColorBrightnessTextBox, FormatPercent(NightColorBrightnessSlider.Value));
        SetTextBoxText(NightColorTintStrengthTextBox, FormatPercent(NightColorTintStrengthSlider.Value));
        SetTextBoxText(NightColorTransitionDurationTextBox, FormatDurationMinutes(TimeSpan.FromMinutes(NightColorTransitionDurationSlider.Value)));
        var now = DateTimeOffset.Now;
        var nightColorState = NightColorEngine.Evaluate(_controller.Settings.NightColor, now);
        NightColorStatusText.Text = _controller.Settings.NightColor.Enabled
            ? NightColorStatus(_controller.Settings.NightColor.Clamp(), nightColorState, now)
            : "Night color off";
        RenderNightColorSummary();
    }

    private void RenderNightColorSummary(DateTimeOffset? previewTimestamp = null)
    {
        var options = _controller.Settings.NightColor.Clamp();
        var isPreview = previewTimestamp is not null;
        var displayOptions = isPreview
            ? options with { Enabled = true, PausedUntilUtc = null }
            : options;
        var now = previewTimestamp ?? DateTimeOffset.Now;
        var state = NightColorEngine.Evaluate(displayOptions, now);
        var progress = displayOptions.Enabled ? Math.Clamp(state.NightProgress, 0, 1) : 0;
        var hasSolarCycle = NightColorEngine.TryGetSunCycle(displayOptions, now, out var sunCycle);

        NightColorTemperatureValueText.Text = NightColorHeadlineText(displayOptions, state);
        NightColorReasonText.Text = isPreview
            ? $"Preview {FormatMinuteOfDay(MinuteOfDay(now))}: {NightColorSummaryText(displayOptions, state, sunCycle, hasSolarCycle)}"
            : NightColorSummaryText(displayOptions, state, sunCycle, hasSolarCycle);
        NightColorProgressFill.Width = 210 * progress;
        NightColorProgressFill.Opacity = displayOptions.Enabled && !IsPausedNightColorState(state) ? 1 : 0.35;
        NightColorProgressText.Text = NightColorProgressStateText(displayOptions, state, progress);
        NightColorPreviewSwatch.Opacity = displayOptions.Enabled ? 1 : 0.46;
        NightColorSunriseText.Text = FormatMinuteOfDay(sunCycle.SunriseMinuteOfDay);
        NightColorSunsetText.Text = FormatMinuteOfDay(sunCycle.SunsetMinuteOfDay);
        NightColorNextChangeText.Text = FormatNextNightColorChange(displayOptions, state, now, sunCycle);
        RenderNightColorPreviewTimeline(displayOptions, now, state, isPreview);

        var controlNow = DateTimeOffset.Now;
        var controlOptions = options;
        var controlState = NightColorEngine.Evaluate(controlOptions, controlNow);
        NightColorEngine.TryGetSunCycle(controlOptions, controlNow, out var controlSunCycle);
        RenderNightColorPauseControls(controlOptions, controlState, controlNow, controlSunCycle);

        if (isPreview)
        {
            NightColorStatusText.Text = $"Preview only - {NightColorStatus(displayOptions, state, now)}";
        }
        else
        {
            SettingsNightColorText.Text = displayOptions.Enabled ? NightColorReasonText.Text : "Night color off";
        }
    }

    private void RenderNightColorPreviewTimeline(
        NightColorOptions options,
        DateTimeOffset timestamp,
        NightColorState state,
        bool isPreview)
    {
        var timelineWidth = NightColorPreviewTimeline.ActualWidth > 1 ? NightColorPreviewTimeline.ActualWidth : 514;
        var actualNow = DateTimeOffset.Now;
        var actualRatio = MinuteOfDay(actualNow) / 1440.0;
        var previewRatio = MinuteOfDay(timestamp) / 1440.0;
        Canvas.SetLeft(NightColorNowMarker, Math.Clamp((timelineWidth * actualRatio) - 1, 0, timelineWidth - 2));
        Canvas.SetLeft(NightColorPreviewMarker, Math.Clamp((timelineWidth * previewRatio) - 4, 0, timelineWidth - 8));
        NightColorPreviewGlow.Width = Math.Clamp(timelineWidth * previewRatio, 0, timelineWidth);
        NightColorPreviewMarker.Opacity = isPreview ? 1 : 0.72;
        NightColorPreviewMarker.Background = PreviewMarkerBrush(state);
        NightColorNowText.Text = FormatMinuteOfDay(MinuteOfDay(actualNow));
        NightColorPreviewClockText.Text = isPreview ? FormatMinuteOfDay(MinuteOfDay(timestamp)) : "Ready";
        NightColorPreviewKelvinText.Text = options.Enabled ? $"{FormatKelvin(state.TemperatureKelvin)} / {FormatPercent(state.Brightness)}" : "Off";
    }

    private void RenderNightColorPauseControls(
        NightColorOptions options,
        NightColorState state,
        DateTimeOffset now,
        NightColorSunCycle sunCycle)
    {
        var clamped = options.Clamp();
        var userPaused = IsNightColorUserPaused(clamped, now);
        var pauseAvailable = clamped.Enabled && state.Enabled && !userPaused && !IsAutoPausedNightColorState(state);
        var resumeAvailable = clamped.Enabled && userPaused;

        PauseNightColorButton.Visibility = pauseAvailable ? Visibility.Visible : Visibility.Collapsed;
        PauseNightColorButton.IsEnabled = pauseAvailable;
        PauseNightColorButtonText.Text = "Pause 1 hour";
        PauseNightColorButtonIcon.Foreground = ResourceBrush("AccentHoverBrush");

        ResumeNightColorButton.Visibility = resumeAvailable ? Visibility.Visible : Visibility.Collapsed;
        ResumeNightColorButton.IsEnabled = resumeAvailable;
        ResumeNightColorButtonText.Text = "Resume now";
        ResumeNightColorButtonIcon.Foreground = ResourceBrush("SuccessBrush");

        string stateText;
        string detailText;
        string icon;
        string softBrush;
        string accentBrush;

        if (!clamped.Enabled)
        {
            stateText = "Off";
            detailText = "Night Color is disabled.";
            icon = "\uE711";
            softBrush = "DangerSoftBrush";
            accentBrush = "DangerBrush";
        }
        else if (userPaused && clamped.PausedUntilUtc is { } pausedUntil)
        {
            stateText = "Paused";
            detailText = $"Paused until {pausedUntil.ToLocalTime():HH:mm}.";
            icon = "\uE769";
            softBrush = "AmberSoftBrush";
            accentBrush = "AmberBrush";
        }
        else if (state.Reason == "fullscreen-paused")
        {
            stateText = "Auto-paused";
            detailText = "Full-screen app is active.";
            icon = "\uE769";
            softBrush = "AmberSoftBrush";
            accentBrush = "AmberBrush";
        }
        else if (state.Reason == "application-exception")
        {
            stateText = "Auto-paused";
            detailText = "Focused app rule is active.";
            icon = "\uE787";
            softBrush = "AmberSoftBrush";
            accentBrush = "AmberBrush";
        }
        else if (state.Enabled)
        {
            stateText = "Active";
            detailText = $"{FormatKelvin(state.TemperatureKelvin)} / {FormatPercent(state.Brightness)} now.";
            icon = "\uE708";
            softBrush = "SuccessSoftBrush";
            accentBrush = "SuccessBrush";
        }
        else
        {
            stateText = "Waiting";
            detailText = FormatNextNightColorChange(clamped, state, now, sunCycle);
            icon = "\uE708";
            softBrush = "ActiveNavBrush";
            accentBrush = "AccentHoverBrush";
        }

        NightColorPauseStateText.Text = stateText;
        NightColorPauseStateDetailText.Text = detailText;
        NightColorPauseStateIcon.Text = icon;
        NightColorPauseStateBadge.Background = ResourceBrush(softBrush);
        NightColorPauseStateBadge.BorderBrush = ResourceBrush(accentBrush);
        NightColorPauseStateIcon.Foreground = ResourceBrush(accentBrush);
        NightColorPauseStateText.Foreground = ResourceBrush(accentBrush);
    }

    private void UpdateSettingControlAvailability()
    {
        RedFlashCheckBox.IsEnabled = FlashGuardCheckBox.IsChecked == true;
        StaticShadePresetComboBox.IsEnabled = StaticShadeCheckBox.IsChecked == true;
        FocusOpacitySlider.IsEnabled = FocusDimmingCheckBox.IsChecked == true;
        TintModeComboBox.IsEnabled = TintCheckBox.IsChecked == true;
        TintOpacitySlider.IsEnabled = TintCheckBox.IsChecked == true;
        NightColorModeComboBox.IsEnabled = true;
        NightColorTemperatureSlider.IsEnabled = true;
        NightColorTemperatureTextBox.IsEnabled = true;
        NightColorDayTemperatureSlider.IsEnabled = true;
        NightColorDayTemperatureTextBox.IsEnabled = true;
        NightColorNightTemperatureSlider.IsEnabled = true;
        NightColorNightTemperatureTextBox.IsEnabled = true;
        NightColorBrightnessSlider.IsEnabled = true;
        NightColorBrightnessTextBox.IsEnabled = true;
        NightColorTintStrengthSlider.IsEnabled = true;
        NightColorTintStrengthTextBox.IsEnabled = true;
        NightColorLocationTextBox.IsEnabled = true;
        NightColorComfortComboBox.IsEnabled = true;
        NightColorTransitionComboBox.IsEnabled = true;
        var smoothTransitionEnabled = NightColorTransitionComboBox.SelectedIndex != 1;
        NightColorTransitionDurationSlider.IsEnabled = smoothTransitionEnabled;
        NightColorTransitionDurationTextBox.IsEnabled = smoothTransitionEnabled;
        var scheduleMode = NightColorModeFromIndex(NightColorModeComboBox.SelectedIndex);
        var automaticScheduleEnabled = scheduleMode != NightColorScheduleMode.Manual;
        NightColorCustomStartTextBox.IsEnabled = automaticScheduleEnabled;
        NightColorCustomEndTextBox.IsEnabled = automaticScheduleEnabled;
        NightColorComfortComboBox.IsEnabled = true;
        NightColorLocationTextBox.IsEnabled = true;
        NightColorLatitudeTextBox.IsEnabled = false;
        NightColorLongitudeTextBox.IsEnabled = false;
        NightColorPauseFullScreenCheckBox.IsEnabled = true;
    }

    private static ProtectionStrengthPreset ProtectionStrengthFromIndex(int index) =>
        index switch
        {
            0 => ProtectionStrengthPreset.Gentle,
            2 => ProtectionStrengthPreset.Strong,
            3 => ProtectionStrengthPreset.Maximum,
            _ => ProtectionStrengthPreset.Balanced
        };

    private static ProtectionPreset ProtectionPresetFromIndex(int index) =>
        index switch
        {
            0 => ProtectionPreset.Comfort,
            1 => ProtectionPreset.Balanced,
            2 => ProtectionPreset.Strong,
            3 => ProtectionPreset.Maximum,
            4 => ProtectionPreset.OledCare,
            _ => ProtectionPreset.Custom
        };

    private static int ProtectionPresetIndex(ProtectionPreset preset) =>
        preset switch
        {
            ProtectionPreset.Comfort => 0,
            ProtectionPreset.Balanced => 1,
            ProtectionPreset.Strong => 2,
            ProtectionPreset.Maximum => 3,
            ProtectionPreset.OledCare => 4,
            _ => 5
        };

    private static string ProtectionPresetDisplayName(ProtectionPreset preset) =>
        preset switch
        {
            ProtectionPreset.Comfort => "Comfort",
            ProtectionPreset.Strong => "Strong",
            ProtectionPreset.Maximum => "Maximum",
            ProtectionPreset.OledCare => "OLED Care",
            ProtectionPreset.Custom => "Custom",
            _ => "Balanced"
        };

    private static (string Icon, string Detail, string SoftBrush, string AccentBrush) ProtectionPresetVisual(ProtectionPreset preset) =>
        preset switch
        {
            ProtectionPreset.Comfort => ("\uE706", "Gentle caps with calm local masking.", "SuccessSoftBrush", "SuccessBrush"),
            ProtectionPreset.Strong => ("\uE945", "Fast response with stronger local masks.", "AmberSoftBrush", "AmberBrush"),
            ProtectionPreset.Maximum => ("\uE7BA", "Most aggressive spike, flash, and surface protection.", "DangerSoftBrush", "DangerBrush"),
            ProtectionPreset.OledCare => ("\uE7F4", "Static and idle protection tuned for OLED.", "AmberSoftBrush", "AmberBrush"),
            ProtectionPreset.Custom => ("\uE713", "Detailed tuning has been customized.", "SurfaceAltBrush", "MutedBrush"),
            _ => ("\uE9CA", "Everyday balance for browsing and apps.", "ActiveNavBrush", "AccentHoverBrush")
        };

    private static ProtectionResponsePreset ProtectionResponseFromIndex(int index) =>
        index switch
        {
            0 => ProtectionResponsePreset.Calm,
            2 => ProtectionResponsePreset.Fast,
            _ => ProtectionResponsePreset.Balanced
        };

    private static LocalProtectionPreset LocalProtectionFromIndex(int index) =>
        index switch
        {
            0 => LocalProtectionPreset.Calm,
            2 => LocalProtectionPreset.Strong,
            3 => LocalProtectionPreset.Aggressive,
            _ => LocalProtectionPreset.Balanced
        };

    private static LocalMaskFitPreset LocalMaskFitFromIndex(int index) =>
        index switch
        {
            1 => LocalMaskFitPreset.Balanced,
            2 => LocalMaskFitPreset.Expanded,
            _ => LocalMaskFitPreset.Precise
        };

    private static WhitePageProtectionPreset WhitePageProtectionFromIndex(int index) =>
        index switch
        {
            0 => WhitePageProtectionPreset.Light,
            2 => WhitePageProtectionPreset.Strong,
            _ => WhitePageProtectionPreset.Normal
        };

    private static StaticShadePreset StaticShadePresetFromIndex(int index) =>
        index switch
        {
            1 => StaticShadePreset.OledConservative,
            2 => StaticShadePreset.OledAggressive,
            3 => StaticShadePreset.StaticHud,
            4 => StaticShadePreset.OfficeUse,
            5 => StaticShadePreset.Disabled,
            _ => StaticShadePreset.OledBalanced
        };

    private static int StaticShadePresetIndex(StaticShadePreset preset) =>
        preset switch
        {
            StaticShadePreset.OledConservative => 1,
            StaticShadePreset.OledAggressive => 2,
            StaticShadePreset.StaticHud => 3,
            StaticShadePreset.OfficeUse => 4,
            StaticShadePreset.Disabled => 5,
            _ => 0
        };

    private void RenderMonitorManualDimmingControls()
    {
        var monitors = _controller.Monitors().ToArray();
        var monitorIds = monitors.Select(monitor => monitor.StableId).ToArray();
        var existingIds = MonitorDimmingComboBox.Items
            .OfType<MonitorListItem>()
            .Select(item => item.Id)
            .ToArray();
        var selectedId = SelectedMonitorManualDimmingId();
        var rebuildList = !existingIds.SequenceEqual(monitorIds, StringComparer.OrdinalIgnoreCase);
        var wasUpdating = _updatingControls;
        _updatingControls = true;
        try
        {
            if (rebuildList)
            {
                MonitorDimmingComboBox.Items.Clear();
                foreach (var monitor in monitors)
                {
                    MonitorDimmingComboBox.Items.Add(new MonitorListItem(monitor.StableId, FormatMonitorListItem(monitor)));
                }
            }

            var selectionTarget = selectedId ?? _selectedMonitorManualDimmingId;
            var selectedIndex = Array.FindIndex(monitors, monitor =>
                string.Equals(monitor.StableId, selectionTarget, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(monitor.DeviceName, selectionTarget, StringComparison.OrdinalIgnoreCase));
            MonitorDimmingComboBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : monitors.Length > 0 ? 0 : -1;
            _selectedMonitorManualDimmingId = SelectedMonitorManualDimmingId();
        }
        finally
        {
            _updatingControls = wasUpdating;
        }

        RenderSelectedMonitorManualDimmingValue();
    }

    private void RenderSelectedMonitorManualDimmingValue()
    {
        var monitor = SelectedMonitorManualDimmingMonitor();
        var entry = monitor is null ? null : _controller.Settings.MonitorManualDimming.FindForMonitor(monitor);
        var opacity = entry?.Opacity ?? _controller.Settings.ManualDimmingOpacity;
        var wasUpdating = _updatingControls;
        _updatingControls = true;
        try
        {
            MonitorManualDimmingSlider.IsEnabled = monitor is not null;
            ClearMonitorDimmingButton.IsEnabled = entry is not null;
            if (monitor is null)
            {
                MonitorManualDimmingSlider.Value = 0;
                MonitorManualDimmingText.Text = "No monitors";
            }
            else
            {
                if (Math.Abs(MonitorManualDimmingSlider.Value - opacity) > 0.001)
                {
                    MonitorManualDimmingSlider.Value = opacity;
                }

                MonitorManualDimmingText.Text = entry is null
                    ? $"All-monitor value {FormatPercent(opacity)}"
                    : FormatPercent(opacity);
            }
        }
        finally
        {
            _updatingControls = wasUpdating;
        }
    }

    private MonitorDescriptor? SelectedMonitorManualDimmingMonitor()
    {
        var monitorId = SelectedMonitorManualDimmingId();
        if (string.IsNullOrWhiteSpace(monitorId))
        {
            return null;
        }

        return _controller.Monitors().FirstOrDefault(monitor =>
            string.Equals(monitor.StableId, monitorId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(monitor.DeviceName, monitorId, StringComparison.OrdinalIgnoreCase));
    }

    private string? SelectedMonitorManualDimmingId() =>
        (MonitorDimmingComboBox.SelectedItem as MonitorListItem)?.Id ?? _selectedMonitorManualDimmingId;

    private void RenderProfiles()
    {
        var wasUpdating = _updatingControls;
        _updatingControls = true;
        try
        {
            var profiles = _controller.Settings.Profiles.Clamp().Entries;
            ProfileComboBox.Items.Clear();
            foreach (var profile in profiles)
            {
                ProfileComboBox.Items.Add(new ProfileListItem(profile.Id, $"{profile.Name} ({profile.Id})"));
            }

            var selectedIndex = Array.FindIndex(
                profiles,
                profile => string.Equals(profile.Id, _selectedProfileId, StringComparison.OrdinalIgnoreCase));
            ProfileComboBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : profiles.Length > 0 ? 0 : -1;

            if (ProfileComboBox.SelectedItem is ProfileListItem item)
            {
                _selectedProfileId = item.Id;
                RenderSelectedProfileFields(item.Id);
            }
            else
            {
                ProfileIdTextBox.Text = string.Empty;
                ProfileNameTextBox.Text = string.Empty;
            }

            UpdateProfileButtonsAvailability();
        }
        finally
        {
            _updatingControls = wasUpdating;
        }
    }

    private void RenderSelectedProfileFields(string id)
    {
        var profile = _controller.Settings.Profiles.Find(id);
        if (profile is null)
        {
            ProfileIdTextBox.Text = id;
            ProfileNameTextBox.Text = string.Empty;
            return;
        }

        ProfileIdTextBox.Text = profile.Id;
        ProfileNameTextBox.Text = profile.Name;
    }

    private string? SelectedProfileId() =>
        string.IsNullOrWhiteSpace(ProfileIdTextBox.Text)
            ? (ProfileComboBox.SelectedItem as ProfileListItem)?.Id
            : ProfileIdTextBox.Text.Trim();

    private void UpdateProfileButtonsAvailability()
    {
        var hasProfile = _controller.Settings.Profiles.Find(SelectedProfileId()) is not null;
        ApplyProfileButton.IsEnabled = hasProfile;
        RemoveProfileButton.IsEnabled = hasProfile;
    }

    private void RenderApplicationRules()
    {
        var wasUpdating = _updatingControls;
        _updatingControls = true;
        try
        {
            var options = _controller.Settings.ApplicationRules.Clamp();
            ApplicationRulesCheckBox.IsChecked = options.Enabled;
            ApplicationRuleComboBox.Items.Clear();
            foreach (var rule in options.Entries)
            {
                ApplicationRuleComboBox.Items.Add(new ApplicationRuleListItem(rule.Id, $"{rule.Name} ({rule.Id})"));
            }

            var selectedIndex = Array.FindIndex(
                options.Entries,
                rule => string.Equals(rule.Id, _selectedApplicationRuleId, StringComparison.OrdinalIgnoreCase));
            ApplicationRuleComboBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : options.Entries.Length > 0 ? 0 : -1;

            if (ApplicationRuleComboBox.SelectedItem is ApplicationRuleListItem item)
            {
                _selectedApplicationRuleId = item.Id;
                RenderSelectedApplicationRuleFields(item.Id);
            }
            else
            {
                ApplicationRuleIdTextBox.Text = string.Empty;
                ApplicationRuleNameTextBox.Text = string.Empty;
                ApplicationRuleProcessTextBox.Text = string.Empty;
                ApplicationRuleTitleTextBox.Text = string.Empty;
                ApplicationRuleEnabledCheckBox.IsChecked = true;
                ApplicationRuleNightColorCheckBox.IsChecked = false;
            }

            var enabledRuleCount = options.Entries.Count(rule => rule.Enabled);
            var nightColorPauseCount = options.Entries.Count(rule => rule.Enabled && rule.ExcludeNightColor);
            RuleCountText.Text = options.Entries.Length.ToString("N0", CultureInfo.InvariantCulture);
            RuleStatusText.Text = options.Enabled ? $"{enabledRuleCount:N0} active" : "Rules off";
            RuleStatusDot.Fill = ResourceBrush(options.Enabled ? "SuccessBrush" : "MutedBrush");
            RuleNightColorText.Text = nightColorPauseCount > 0
                ? $"{nightColorPauseCount:N0} pause Night Color"
                : "No pauses";
            RuleSelectedText.Text = ApplicationRuleComboBox.SelectedItem is ApplicationRuleListItem selectedRule
                ? selectedRule.DisplayName
                : "None";
            UpdateApplicationRuleButtonsAvailability();
        }
        finally
        {
            _updatingControls = wasUpdating;
        }
    }

    private void RenderSelectedApplicationRuleFields(string id)
    {
        var rule = _controller.Settings.ApplicationRules.Find(id);
        if (rule is null)
        {
            ApplicationRuleIdTextBox.Text = id;
            ApplicationRuleNameTextBox.Text = string.Empty;
            ApplicationRuleProcessTextBox.Text = string.Empty;
            ApplicationRuleTitleTextBox.Text = string.Empty;
            ApplicationRuleEnabledCheckBox.IsChecked = true;
            ApplicationRuleNightColorCheckBox.IsChecked = false;
            return;
        }

        ApplicationRuleIdTextBox.Text = rule.Id;
        ApplicationRuleNameTextBox.Text = rule.Name;
        ApplicationRuleProcessTextBox.Text = rule.ProcessName ?? string.Empty;
        ApplicationRuleTitleTextBox.Text = rule.WindowTitleContains ?? string.Empty;
        ApplicationRuleEnabledCheckBox.IsChecked = rule.Enabled;
        ApplicationRuleNightColorCheckBox.IsChecked = rule.ExcludeNightColor;
    }

    private string? SelectedApplicationRuleId() =>
        string.IsNullOrWhiteSpace(ApplicationRuleIdTextBox.Text)
            ? (ApplicationRuleComboBox.SelectedItem as ApplicationRuleListItem)?.Id
            : ApplicationRuleIdTextBox.Text.Trim();

    private void UpdateApplicationRuleButtonsAvailability()
    {
        var hasRule = _controller.Settings.ApplicationRules.Find(SelectedApplicationRuleId()) is not null;
        var hasCriteria = !string.IsNullOrWhiteSpace(ApplicationRuleProcessTextBox.Text) ||
            !string.IsNullOrWhiteSpace(ApplicationRuleTitleTextBox.Text);
        SaveApplicationRuleButton.IsEnabled =
            !string.IsNullOrWhiteSpace(ApplicationRuleIdTextBox.Text) &&
            hasCriteria;
        RemoveApplicationRuleButton.IsEnabled = hasRule;
        ClearApplicationRulesButton.IsEnabled = _controller.Settings.ApplicationRules.Clamp().Entries.Length > 0;
    }

    private void MarkSettingsChanged()
    {
        if (_updatingControls)
        {
            return;
        }

        if (!_hasUnsavedChanges)
        {
            _hasUnsavedChanges = true;
        }

        FooterText.Text = "Settings updated.";
    }

    private static int TintModeIndex(TintOptions tint) => tint.Green >= 230 ? 1 : 0;

    private static NightColorScheduleMode NightColorModeFromIndex(int index) => index switch
    {
        1 => NightColorScheduleMode.Manual,
        2 => NightColorScheduleMode.SunriseSunset,
        _ => NightColorScheduleMode.Custom
    };

    private static int NightColorModeIndex(NightColorScheduleMode mode) => mode switch
    {
        NightColorScheduleMode.Manual => 1,
        NightColorScheduleMode.SunriseSunset => 2,
        _ => 0
    };

    private static (int NightTemperatureKelvin, double Brightness, double TintStrength) NightColorComfortFromIndex(int index) =>
        index switch
        {
            0 => (4100, 0.97, 0.70),
            2 => (3000, 0.90, 1.00),
            _ => (3600, 0.94, 0.86)
        };

    private static int NightColorComfortIndex(NightColorOptions options)
    {
        var clamped = options.Clamp();
        if (clamped.NightTemperatureKelvin >= 3950 || clamped.TintStrength <= 0.72)
        {
            return 0;
        }

        return clamped.NightTemperatureKelvin <= 3200 || clamped.Brightness <= 0.91 ? 2 : 1;
    }

    private static string NightColorStatus(NightColorOptions options, NightColorState state, DateTimeOffset now)
    {
        var mode = options.ScheduleMode switch
        {
            NightColorScheduleMode.Manual => "Always comfort",
            NightColorScheduleMode.SunriseSunset => "City sunrise",
            _ => "Automatic"
        };
        var schedule = NightColorScheduleStatus(options, now);
        var activity = state.Enabled ? "active" : "waiting";
        return $"{mode} - {DisplayNightColorLocation(options)} - {schedule} - {activity}";
    }

    private static string NightColorScheduleStatus(NightColorOptions options, DateTimeOffset now)
    {
        if (options.ScheduleMode == NightColorScheduleMode.Manual)
        {
            return "always on";
        }

        if (options.ScheduleMode == NightColorScheduleMode.SunriseSunset &&
            NightColorEngine.TryGetSunCycle(options, now, out var sunCycle))
        {
            return $"{FormatMinuteOfDay(sunCycle.SunsetMinuteOfDay)} -> {FormatMinuteOfDay(sunCycle.SunriseMinuteOfDay)}";
        }

        return $"{FormatMinuteOfDay(options.CustomStartMinuteOfDay)} -> {FormatMinuteOfDay(options.CustomEndMinuteOfDay)}";
    }

    private static string NightColorSummaryText(
        NightColorOptions options,
        NightColorState state,
        NightColorSunCycle sunCycle,
        bool hasSolarCycle)
    {
        if (!options.Enabled)
        {
            return "Night Color is off.";
        }

        if (state.Reason == "paused" && options.PausedUntilUtc is { } pausedUntil)
        {
            return $"Paused until {pausedUntil.ToLocalTime():HH:mm}.";
        }

        if (state.Reason == "fullscreen-paused")
        {
            return "Paused while the full-screen app is active.";
        }

        if (state.Reason == "application-exception")
        {
            return "Paused by the focused app rule.";
        }

        if (options.ScheduleMode == NightColorScheduleMode.Manual)
        {
            return state.Enabled
                ? $"Comfort color is always on at {FormatKelvin(state.TemperatureKelvin)}."
                : "Comfort color is ready.";
        }

        var scheduleName = options.ScheduleMode == NightColorScheduleMode.SunriseSunset && hasSolarCycle
            ? $"{DisplayNightColorLocation(options)} sunset"
            : "sleep time";
        var start = options.ScheduleMode == NightColorScheduleMode.SunriseSunset
            ? sunCycle.SunsetMinuteOfDay
            : options.CustomStartMinuteOfDay;
        var end = options.ScheduleMode == NightColorScheduleMode.SunriseSunset
            ? sunCycle.SunriseMinuteOfDay
            : options.CustomEndMinuteOfDay;

        return state.Enabled
            ? $"Following {scheduleName}; easing back by {FormatMinuteOfDay(end)}."
            : $"Waiting for {FormatMinuteOfDay(start)}.";
    }

    private static string NightColorHeadlineText(NightColorOptions options, NightColorState state)
    {
        if (!options.Enabled)
        {
            return "Off";
        }

        if (IsPausedNightColorState(state))
        {
            return "Paused";
        }

        return FormatKelvin(state.TemperatureKelvin);
    }

    private static string NightColorProgressStateText(NightColorOptions options, NightColorState state, double progress)
    {
        if (!options.Enabled)
        {
            return "Off";
        }

        if (IsPausedNightColorState(state))
        {
            return "Paused";
        }

        return state.Enabled ? $"{FormatPercent(progress)} active" : "Waiting";
    }

    private static string FormatNextNightColorChange(
        NightColorOptions options,
        NightColorState state,
        DateTimeOffset now,
        NightColorSunCycle sunCycle)
    {
        if (!options.Enabled)
        {
            return "Off";
        }

        if (state.Reason == "paused" && options.PausedUntilUtc is { } pausedUntil)
        {
            return $"Paused until {pausedUntil.ToLocalTime():HH:mm}";
        }

        if (options.ScheduleMode == NightColorScheduleMode.Manual)
        {
            return "Always on";
        }

        var start = options.ScheduleMode == NightColorScheduleMode.SunriseSunset
            ? sunCycle.SunsetMinuteOfDay
            : options.CustomStartMinuteOfDay;
        var end = options.ScheduleMode == NightColorScheduleMode.SunriseSunset
            ? sunCycle.SunriseMinuteOfDay
            : options.CustomEndMinuteOfDay;
        var current = MinuteOfDay(now);
        var inNightWindow = IsInForwardWindow(start, end, current);
        var target = inNightWindow ? end : start;
        var label = inNightWindow
            ? options.ScheduleMode == NightColorScheduleMode.SunriseSunset ? "Sunrise" : "Wake"
            : options.ScheduleMode == NightColorScheduleMode.SunriseSunset ? "Sunset" : "Sleep";
        var minutes = ForwardMinutes(current, target);
        return $"{label} {FormatMinuteOfDay(target)} ({FormatRelativeMinutes(minutes)})";
    }

    private static bool IsNightColorUserPaused(NightColorOptions options, DateTimeOffset now) =>
        options.PausedUntilUtc is { } pausedUntil && pausedUntil.ToUniversalTime() > now.ToUniversalTime();

    private static bool IsPausedNightColorState(NightColorState state) =>
        state.Reason == "paused" || IsAutoPausedNightColorState(state);

    private static bool IsAutoPausedNightColorState(NightColorState state) =>
        state.Reason is "fullscreen-paused" or "application-exception";

    private static string DisplayNightColorLocation(NightColorOptions options) =>
        string.IsNullOrWhiteSpace(options.LocationName) ? "Istanbul" : options.LocationName;

    private static string FormatPercent(double value) => $"{Math.Round(value * 100):0}%";

    private static string FormatKelvin(double value) =>
        $"{Math.Round(value).ToString("0", CultureInfo.InvariantCulture)}K";

    private static string FormatDurationMinutes(TimeSpan duration) =>
        $"{Math.Round(duration.TotalMinutes).ToString("0", CultureInfo.InvariantCulture)} min";

    private static string FormatMinuteOfDay(int minuteOfDay)
    {
        var normalized = ((minuteOfDay % 1440) + 1440) % 1440;
        return $"{(normalized / 60).ToString("00", CultureInfo.InvariantCulture)}:{(normalized % 60).ToString("00", CultureInfo.InvariantCulture)}";
    }

    private static int MinuteOfDay(DateTimeOffset timestamp) => (timestamp.Hour * 60) + timestamp.Minute;

    private static int ForwardMinutes(int fromMinuteOfDay, int toMinuteOfDay)
    {
        var normalizedFrom = ((fromMinuteOfDay % 1440) + 1440) % 1440;
        var normalizedTo = ((toMinuteOfDay % 1440) + 1440) % 1440;
        return (normalizedTo - normalizedFrom + 1440) % 1440;
    }

    private static bool IsInForwardWindow(int startMinuteOfDay, int endMinuteOfDay, int currentMinuteOfDay)
    {
        if (startMinuteOfDay == endMinuteOfDay)
        {
            return false;
        }

        return ForwardMinutes(startMinuteOfDay, currentMinuteOfDay) < ForwardMinutes(startMinuteOfDay, endMinuteOfDay);
    }

    private static string FormatRelativeMinutes(int minutes)
    {
        if (minutes <= 0)
        {
            return "now";
        }

        if (minutes < 60)
        {
            return $"{minutes.ToString("0", CultureInfo.InvariantCulture)} min";
        }

        var hours = minutes / 60;
        var remainingMinutes = minutes % 60;
        return remainingMinutes == 0
            ? $"{hours.ToString("0", CultureInfo.InvariantCulture)}h"
            : $"{hours.ToString("0", CultureInfo.InvariantCulture)}h {remainingMinutes.ToString("0", CultureInfo.InvariantCulture)}m";
    }

    private static System.Windows.Media.Brush PreviewMarkerBrush(NightColorState state)
    {
        if (!state.Tint.Enabled)
        {
            return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(115, 167, 255));
        }

        return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(
            state.Tint.Red,
            state.Tint.Green,
            state.Tint.Blue));
    }

    private System.Windows.Media.Brush ResourceBrush(string key) =>
        (System.Windows.Media.Brush)FindResource(key);

    private static string FormatCoordinate(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static bool TryParseKelvin(string? value, out int kelvin)
    {
        kelvin = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().TrimEnd('K', 'k');
        if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        kelvin = Math.Clamp(parsed, 1000, 12000);
        return true;
    }

    private static bool TryParseTransitionDuration(string? value, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant().Replace(',', '.');
        var isHours = normalized.EndsWith("h", StringComparison.Ordinal);
        normalized = normalized
            .Replace("minutes", string.Empty, StringComparison.Ordinal)
            .Replace("minute", string.Empty, StringComparison.Ordinal)
            .Replace("mins", string.Empty, StringComparison.Ordinal)
            .Replace("min", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (normalized.EndsWith("m", StringComparison.Ordinal) || normalized.EndsWith("h", StringComparison.Ordinal))
        {
            normalized = normalized[..^1].Trim();
        }

        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        var minutes = isHours ? parsed * 60 : parsed;
        duration = TimeSpan.FromMinutes(Math.Clamp(minutes, 0, 360));
        return true;
    }

    private static bool TryParseClockMinute(string? value, out int minuteOfDay)
    {
        minuteOfDay = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!TimeOnly.TryParseExact(
                value.Trim(),
                ClockInputFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time))
        {
            return false;
        }

        minuteOfDay = time.Hour * 60 + time.Minute;
        return true;
    }

    private static bool TryParsePercent(string? value, double minimum, double maximum, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().TrimEnd('%').Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        var valueAsRatio = parsed > 1 ? parsed / 100.0 : parsed;
        result = Math.Clamp(valueAsRatio, minimum, maximum);
        return true;
    }

    private static bool TryParseCoordinate(string? value, double minimum, double maximum, out double coordinate)
    {
        coordinate = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        coordinate = Math.Clamp(parsed, minimum, maximum);
        return true;
    }

    private static void SetTextBoxText(System.Windows.Controls.TextBox textBox, string value)
    {
        if (!string.Equals(textBox.Text, value, StringComparison.Ordinal))
        {
            textBox.Text = value;
        }
    }

    private static string FormatMonitorListItem(MonitorDescriptor monitor) =>
        string.IsNullOrWhiteSpace(monitor.DisplayName)
            ? monitor.DeviceName
            : $"{monitor.DisplayName} ({monitor.DeviceName})";

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? StartupExecutablePath()
    {
        if (IsLumaBrakeAppExecutable(Environment.ProcessPath))
        {
            return Environment.ProcessPath;
        }

        var appPath = Path.Combine(AppContext.BaseDirectory, "LumaBrake.App.exe");
        return IsLumaBrakeAppExecutable(appPath) ? appPath : null;
    }

    private static bool IsLumaBrakeAppExecutable(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        File.Exists(path) &&
        string.Equals(Path.GetFileName(path), "LumaBrake.App.exe", StringComparison.OrdinalIgnoreCase);

    private void ApplyDarkWindowFrame()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            if (!SetDwmIntAttribute(handle, DwmUseImmersiveDarkMode, 1))
            {
                _ = SetDwmIntAttribute(handle, DwmUseImmersiveDarkModeFallback, 1);
            }

            _ = SetDwmIntAttribute(handle, DwmWindowCornerPreference, DwmRoundCorners);
            _ = SetDwmIntAttribute(handle, DwmBorderColor, DarkBorderColorRef);
            _ = SetDwmIntAttribute(handle, DwmCaptionColor, DarkChromeColorRef);
            _ = SetDwmIntAttribute(handle, DwmTextColor, LightTextColorRef);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private static bool SetDwmIntAttribute(IntPtr handle, int attribute, int value)
    {
        var attributeValue = value;
        return DwmSetWindowAttribute(handle, attribute, ref attributeValue, sizeof(int)) == 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    private static string FormatStartupStatus(StartupRegistrationStatus status)
    {
        if (!status.IsRegistered)
        {
            return "LumaBrake will not start with Windows.";
        }

        return status.IsCurrent
            ? "LumaBrake starts with Windows in the tray."
            : "Windows startup points to a different LumaBrake command.";
    }

    private static bool IsStartupRegistrationException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException;

    private static string FormatSamplingStatus(PipelineSnapshot snapshot)
    {
        if (snapshot.HasActiveCaptureAnomaly)
        {
            return "screen sampling limited";
        }

        if (snapshot.HasActiveCaptureFailure)
        {
            return "screen sampling failed on one display";
        }

        if (snapshot.TransientFailureCount <= 0 &&
            snapshot.CaptureAnomalyEvents <= 0 &&
            snapshot.CaptureFailureEvents <= 0)
        {
            return "screen sampling stable";
        }

        if (snapshot.CaptureFailureEvents > 0)
        {
            var captureFailureTimestamp = snapshot.LastCaptureFailureAt?.ToLocalTime().ToString("HH:mm:ss") ?? "unknown time";
            var captureFailureCount = snapshot.CaptureFailureEvents == 1
                ? "1 failed frame"
                : $"{snapshot.CaptureFailureEvents:N0} failed frames";
            return $"screen sampling recovered from {captureFailureCount}, last {captureFailureTimestamp}";
        }

        if (snapshot.CaptureAnomalyEvents > 0)
        {
            var anomalyTimestamp = snapshot.LastCaptureAnomalyAt?.ToLocalTime().ToString("HH:mm:ss") ?? "unknown time";
            var anomalyCount = snapshot.CaptureAnomalyEvents == 1
                ? "1 limited frame"
                : $"{snapshot.CaptureAnomalyEvents:N0} limited frames";
            return $"screen sampling recovered from {anomalyCount}, last {anomalyTimestamp}";
        }

        var failureTimestamp = snapshot.LastTransientFailureAt?.ToLocalTime().ToString("HH:mm:ss") ?? "unknown time";
        var failureCount = snapshot.TransientFailureCount == 1
            ? "1 time"
            : $"{snapshot.TransientFailureCount:N0} times";
        return $"screen sampling recovered {failureCount}, last {failureTimestamp}";
    }

    private static string FormatAdvancedColor(MonitorAdvancedColorState? state)
    {
        state ??= MonitorAdvancedColorState.Unknown;
        if (!state.IsKnown)
        {
            return "HDR/advanced color: unknown";
        }

        if (state.IsEnabled)
        {
            return $"HDR/advanced color: active ({state.ColorEncoding}, {state.BitsPerColorChannel} bpc)";
        }

        if (state.IsForceDisabled)
        {
            return "HDR/advanced color: disabled by Windows";
        }

        return state.IsSupported
            ? "HDR/advanced color: available"
            : "HDR/advanced color: off";
    }

    private static string FormatReason(EffectReason reason) => reason switch
    {
        EffectReason.BrightnessSpike => "Brightness spike",
        EffectReason.RepetitiveFlash => "Repetitive flash",
        EffectReason.Manual => "Manual dimming",
        EffectReason.StaticShade => "Static shade",
        EffectReason.InactiveMonitor => "Inactive monitor",
        EffectReason.AdaptiveContent => "Adaptive content",
        EffectReason.FocusDimming => "Focus dimming",
        EffectReason.Scheduled => "Scheduled dimming",
        _ => "None"
    };

    private sealed record ProfileListItem(string Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record MonitorListItem(string Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record ApplicationRuleListItem(string Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private readonly record struct BrightnessHistorySample(
        DateTimeOffset Timestamp,
        double AverageLuminance,
        double Percentile95Luminance,
        double OverlayOpacity,
        long ProtectionEvents);
}
