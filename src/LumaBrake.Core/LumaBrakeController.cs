using System.Runtime.InteropServices;
using LumaBrake.Analysis.Detection;
using LumaBrake.Analysis.Frames;
using LumaBrake.Capture;
using LumaBrake.Diagnostics;
using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Color;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Frames;
using LumaBrake.Domain.Monitors;
using LumaBrake.Domain.Rules;
using LumaBrake.Domain.Schedules;
using LumaBrake.Effects.Composition;
using LumaBrake.Persistence;
using LumaBrake.Rendering;

namespace LumaBrake.Core;

public sealed class LumaBrakeController : IDisposable
{
    private const double RegionalFlashFallbackRatio = 0.35;
    private const double LocalizedFlashGlobalFloorMinimumOpacity = 0.18;
    private const double LocalizedFlashGlobalFloorMaximumOpacity = 0.28;
    private const double LocalizedFlashGlobalFloorScale = 0.50;
    private const int CaptureAnomalyActivationFrames = 3;
    private const int CaptureFailureClearThreshold = 3;
    private const int NightColorMaximumKelvinStep = 120;
    private const double NightColorMaximumBrightnessStep = 0.014;
    private const double NightColorMaximumDimmingOpacityStep = 0.020;
    private const double NightColorMaximumProgressStep = 0.045;
    private const double NightColorMaximumTintOpacityStep = 0.018;
    private static readonly TimeSpan DefaultAnalysisInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan DefaultMonitorRefreshInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FocusedMonitorLookupGracePeriod = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan MaximumRegionalClearRelease = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan NightColorPreviewMinimumApplyInterval = TimeSpan.FromMilliseconds(120);

    private readonly IFrameCapture _capture;
    private readonly IOverlayRenderer _overlay;
    private readonly Bgra32FrameAnalyzer _analyzer = new();
    private readonly EffectComposer _composer = new();
    private readonly IMonitorCatalog _monitorCatalog;
    private readonly IFocusedMonitorProvider _focusedMonitorProvider;
    private readonly ICursorMonitorProvider _cursorMonitorProvider;
    private readonly IForegroundApplicationProvider _foregroundApplicationProvider;
    private readonly INightColorDisplayController _nightColorDisplayController;
    private readonly TimeSpan _analysisInterval;
    private readonly TimeSpan _monitorRefreshInterval;
    private readonly Dictionary<string, MonitorPipeline> _pipelines = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _gate = new();
    private Task? _loop;
    private bool _disposed;
    private long _transientFailureCount;
    private DateTimeOffset? _lastTransientFailureAt;
    private DateTimeOffset _nextMonitorRefreshAt = DateTimeOffset.MinValue;
    private IReadOnlyList<MonitorDescriptor> _monitors = Array.Empty<MonitorDescriptor>();
    private string _monitorStatus = "Monitor list has not been refreshed.";
    private int _renderGeneration;
    private string? _lastKnownFocusedMonitorStableId;
    private DateTimeOffset? _lastKnownFocusedMonitorAt;
    private NightColorState? _nightColorPreviewOverride;
    private NightColorState? _lastNightColorOutputState;
    private DateTimeOffset _nextNightColorPreviewApplyAt = DateTimeOffset.MinValue;
    private bool? _lastNightColorPreviewDisplayApplied;

    public LumaBrakeController(
        IFrameCapture capture,
        IOverlayRenderer overlay,
        LumaBrakeSettings settings,
        IMonitorCatalog monitorCatalog,
        IFocusedMonitorProvider? focusedMonitorProvider = null,
        TimeSpan? monitorRefreshInterval = null,
        IForegroundApplicationProvider? foregroundApplicationProvider = null,
        ICursorMonitorProvider? cursorMonitorProvider = null,
        TimeSpan? analysisInterval = null,
        INightColorDisplayController? nightColorDisplayController = null)
    {
        _capture = capture;
        _overlay = overlay;
        Settings = settings;
        _monitorCatalog = monitorCatalog;
        _focusedMonitorProvider = focusedMonitorProvider ?? NoFocusedMonitorProvider.Instance;
        _cursorMonitorProvider = cursorMonitorProvider ?? NoCursorMonitorProvider.Instance;
        _foregroundApplicationProvider = foregroundApplicationProvider ?? NoForegroundApplicationProvider.Instance;
        _nightColorDisplayController = nightColorDisplayController ?? NoNightColorDisplayController.Instance;
        _analysisInterval = NormalizeAnalysisInterval(analysisInterval);
        _monitorRefreshInterval = monitorRefreshInterval ?? DefaultMonitorRefreshInterval;
        RefreshMonitors();
    }

    public LumaBrakeSettings Settings { get; private set; }

    public event EventHandler<PipelineSnapshot>? SnapshotUpdated;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _overlay.SyncMonitors(Monitors());
        _overlay.Show();
        TryHideAll();
        _loop ??= Task.Run(() => RunAsync(_shutdown.Token));
    }

    public void ToggleEnabled()
    {
        Settings = Settings with { Enabled = !Settings.Enabled };
        if (!Settings.Enabled)
        {
            ClearAutomaticEffects(resetManualDimming: false);
        }
    }

    public void SetEnabled(bool enabled)
    {
        Settings = Settings with { Enabled = enabled };
        if (!enabled)
        {
            ClearAutomaticEffects(resetManualDimming: false);
        }
    }

    public void SetUserMaximumOpacity(double maximumOpacity)
    {
        Settings = Settings with { UserMaximumOpacity = Math.Clamp(maximumOpacity, 0.05, 0.92) };
    }

    public void SetManualDimmingOpacity(double opacity)
    {
        Settings = Settings with { ManualDimmingOpacity = Math.Clamp(opacity, 0, 0.90) };
        PublishSnapshot();
    }

    public void SetMonitorManualDimmingOpacity(string monitorId, double opacity)
    {
        var entry = new MonitorManualDimmingOverride { MonitorId = monitorId, Opacity = opacity }.Clamp();
        if (string.IsNullOrWhiteSpace(entry.MonitorId))
        {
            throw new ArgumentException("Monitor ID is required.", nameof(monitorId));
        }

        Settings = Settings with { MonitorManualDimming = Settings.MonitorManualDimming.Upsert(entry) };
        PublishSnapshot();
    }

    public void ClearMonitorManualDimming(string monitorId)
    {
        Settings = Settings with { MonitorManualDimming = Settings.MonitorManualDimming.Remove(monitorId) };
        PublishSnapshot();
    }

    public void ApplySettings(LumaBrakeSettings settings, bool resetDetectorState = false)
    {
        var shouldHideEffects = false;
        var shouldClearNightColor = !settings.NightColor.Clamp().Enabled;
        lock (_gate)
        {
            Settings = settings;
            if (resetDetectorState)
            {
                foreach (var pipeline in _pipelines.Values)
                {
                    pipeline.RebuildDetectors(Settings);
                    ClearPipelineEffectState(pipeline);
                }

                shouldHideEffects = true;
            }

            if (!Settings.Enabled)
            {
                foreach (var pipeline in _pipelines.Values)
                {
                    ClearPipelineEffectState(pipeline);
                }

                shouldHideEffects = true;
            }

            if (shouldHideEffects)
            {
                _renderGeneration++;
            }
        }

        if (shouldClearNightColor)
        {
            TryClearNightColorDisplay();
        }

        if (shouldHideEffects)
        {
            TryHideAll();
        }

        PublishSnapshot();
    }

    public void EmergencyDisable()
    {
        ClearActiveEffects(resetManualDimming: true, disableRuntime: true);
    }

    private void ClearActiveEffects(bool resetManualDimming, bool disableRuntime = false)
    {
        ClearActiveEffectsCore(resetManualDimming, disableRuntime);
        TryClearNightColorDisplay();
        TryHideAll();
        PublishSnapshot();
    }

    private void ClearActiveEffectsCore(bool resetManualDimming, bool disableRuntime = false)
    {
        lock (_gate)
        {
            if (disableRuntime)
            {
                Settings = Settings with
                {
                    Enabled = false,
                    NightColor = Settings.NightColor.Clamp() with
                    {
                        Enabled = false,
                        PausedUntilUtc = null
                    }
                };
            }

            if (resetManualDimming)
            {
                Settings = Settings with
                {
                    ManualDimmingOpacity = 0,
                    MonitorManualDimming = new MonitorManualDimmingOptions()
                };
            }

            foreach (var pipeline in _pipelines.Values)
            {
                ClearPipelineEffectState(pipeline);
            }

            _renderGeneration++;
        }
    }

    public PipelineSnapshot Snapshot()
    {
        lock (_gate)
        {
            var pipelines = _pipelines.Values
                .OrderByDescending(pipeline => pipeline.Monitor.IsPrimary)
                .ThenBy(pipeline => pipeline.Monitor.DeviceName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var activePipeline = pipelines.FirstOrDefault();
            var monitorSnapshots = pipelines.Select(ToSnapshot).ToArray();

            return new PipelineSnapshot(
                DateTimeOffset.UtcNow,
                Settings.Enabled,
                activePipeline?.LastAverage ?? 0,
                activePipeline?.LastP95 ?? 0,
                activePipeline?.LastOpacity ?? 0,
                activePipeline?.LastReason ?? EffectReason.None,
                activePipeline?.Monitor.DeviceName ?? "unknown",
                _monitors.Count,
                _monitorStatus,
                _transientFailureCount,
                _lastTransientFailureAt,
                pipelines.Sum(pipeline => pipeline.CaptureAnomalyEvents),
                pipelines.Select(pipeline => pipeline.LastCaptureAnomalyAt).DefaultIfEmpty().Max(),
                pipelines.Any(pipeline => pipeline.IsCaptureAnomalyActive),
                pipelines.Sum(pipeline => pipeline.CaptureFailureEvents),
                pipelines.Select(pipeline => pipeline.LastCaptureFailureAt).DefaultIfEmpty().Max(),
                pipelines.Any(pipeline => pipeline.IsCaptureFailureActive),
                pipelines.Sum(pipeline => pipeline.FramesAnalyzed),
                pipelines.Sum(pipeline => pipeline.SpikeEvents),
                pipelines.Sum(pipeline => pipeline.FlashEvents),
                pipelines.Sum(pipeline => pipeline.RedFlashEvents),
                pipelines.Sum(pipeline => pipeline.LastRegionalMaskCount),
                pipelines.Select(pipeline => pipeline.LastRegionalMaskMaxOpacity).DefaultIfEmpty(0).Max(),
                monitorSnapshots)
            {
                RegionalMasks = pipelines.SelectMany(pipeline => pipeline.LastRegionalMasks).ToArray()
            };
        }
    }

    public IReadOnlyList<MonitorDescriptor> Monitors()
    {
        lock (_gate)
        {
            return _monitors.ToArray();
        }
    }

    public bool PreviewNightColor(NightColorState state)
    {
        var applyFromCaller = false;
        lock (_gate)
        {
            _nightColorPreviewOverride = state;
            applyFromCaller = _loop is null || _lastNightColorPreviewDisplayApplied is null;
        }

        return applyFromCaller ? ApplyNightColorPreviewState(state) : state.Enabled;
    }

    public void StopNightColorPreview()
    {
        var hadPreview = false;
        lock (_gate)
        {
            hadPreview = _nightColorPreviewOverride is not null;
            _nightColorPreviewOverride = null;
            ResetNightColorPreviewApplyState();
        }

        if (hadPreview)
        {
            RestoreNightColorDisplayState();
        }
    }

    public void RefreshMonitors()
    {
        RefreshMonitorsCore(forcePublish: true);
    }

    public void ClearDisplayStateForSystemEvent()
    {
        TryClearNightColorDisplay();
        TryHideAll();
        PublishSnapshot();
    }

    public void RefreshDisplayStateAfterSystemEvent()
    {
        RefreshMonitors();
    }

    private bool RefreshMonitorsCore(bool forcePublish)
    {
        IReadOnlyList<MonitorDescriptor> monitors;
        string monitorStatus;
        try
        {
            monitors = _monitorCatalog.GetMonitors();
            monitorStatus = $"Detected {monitors.Count} monitor(s).";
        }
        catch (Exception exception) when (IsRecoverableMonitorEnumerationFailure(exception))
        {
            monitors = Array.Empty<MonitorDescriptor>();
            monitorStatus = "Monitor detection failed.";
        }

        var changed = false;
        var resetEffectStableIds = new List<string>();
        var invalidateRenderGeneration = false;
        lock (_gate)
        {
            changed = !MonitorTopologyEquals(_monitors, monitors) ||
                !string.Equals(_monitorStatus, monitorStatus, StringComparison.Ordinal);

            if (!changed && !forcePublish)
            {
                return false;
            }

            _monitors = monitors;
            _monitorStatus = monitorStatus;

            var activeIds = monitors.Select(monitor => monitor.StableId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var removedId in _pipelines.Keys.Where(id => !activeIds.Contains(id)).ToArray())
            {
                _pipelines.Remove(removedId);
                invalidateRenderGeneration = true;
            }

            foreach (var monitor in monitors)
            {
                if (_pipelines.TryGetValue(monitor.StableId, out var pipeline))
                {
                    if (!MonitorRuntimeEquals(pipeline.Monitor, monitor))
                    {
                        pipeline.ResetRuntimeState(Settings);
                        resetEffectStableIds.Add(monitor.StableId);
                        invalidateRenderGeneration = true;
                    }

                    pipeline.Monitor = monitor;
                    continue;
                }

                _pipelines[monitor.StableId] = new MonitorPipeline(monitor, Settings);
                invalidateRenderGeneration = true;
            }

            if (invalidateRenderGeneration)
            {
                _renderGeneration++;
            }
        }

        _overlay.SyncMonitors(monitors);
        foreach (var stableId in resetEffectStableIds)
        {
            _overlay.SetEffect(stableId, FinalEffectState.Clear);
        }

        PublishSnapshot();
        return changed;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _shutdown.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Shutdown should never keep a dimming overlay alive.
        }

        _overlay.Dispose();
        _nightColorDisplayController.Dispose();
        _capture.Dispose();
        _shutdown.Dispose();
        _disposed = true;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_analysisInterval);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                RunOnce();
            }
            catch (Exception)
            {
                RecoverFromTransientLoopFailure();
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }

            if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                break;
            }
        }
    }

    private void RunOnce()
    {
        RefreshMonitorsIfDue(DateTimeOffset.UtcNow);

        if (Settings.Enabled)
        {
            AnalyzeAndRender();
        }
        else
        {
            RenderNightColorOnly();
        }
    }

    private void ClearAutomaticEffects(bool resetManualDimming)
    {
        ClearActiveEffectsCore(resetManualDimming);
        if (!Settings.NightColor.Clamp().Enabled)
        {
            TryClearNightColorDisplay();
        }

        TryHideAll();
        PublishSnapshot();
    }

    private void RecoverFromTransientLoopFailure()
    {
        lock (_gate)
        {
            _transientFailureCount++;
            _lastTransientFailureAt = DateTimeOffset.UtcNow;
        }

        ClearActiveEffectsCore(resetManualDimming: false);
        TryClearNightColorDisplay();
        TryHideAll();
        PublishSnapshot();
    }

    private void TryHideAll()
    {
        try
        {
            _overlay.HideAll();
        }
        catch (Exception)
        {
        }
    }

    private bool TryApplyNightColorDisplay(IReadOnlyList<MonitorDescriptor> monitors, NightColorState nightColor)
    {
        try
        {
            return _nightColorDisplayController.Apply(monitors, nightColor);
        }
        catch (Exception)
        {
            TryClearNightColorDisplay();
            return false;
        }
    }

    private void TryClearNightColorDisplay()
    {
        try
        {
            _nightColorDisplayController.Clear();
        }
        catch (Exception)
        {
        }
    }

    private bool ApplyNightColorPreviewState(NightColorState state)
    {
        MonitorPipeline[] pipelines;
        LumaBrakeSettings settings;

        lock (_gate)
        {
            pipelines = _pipelines.Values.ToArray();
            settings = Settings;
        }

        var activeMonitors = pipelines.Select(pipeline => pipeline.Monitor).ToArray();
        if (activeMonitors.Length == 0)
        {
            TryClearNightColorDisplay();
            return false;
        }

        PrepareNightColorPreviewFrame(
            activeMonitors,
            state,
            DateTimeOffset.UtcNow,
            out var displayState,
            out var displayApplied);
        if (displayApplied)
        {
            ClearNightColorPreviewOverlay(pipelines);
            return true;
        }

        return TryApplyNightColorPreviewOverlay(pipelines, settings, displayState);
    }

    private void PrepareNightColorPreviewFrame(
        IReadOnlyList<MonitorDescriptor> activeMonitors,
        NightColorState target,
        DateTimeOffset now,
        out NightColorState outputState,
        out bool displayApplied)
    {
        if (!ReserveNightColorPreviewApplySlot(now))
        {
            outputState = LastNightColorOutputStateOr(target);
            displayApplied = LastNightColorPreviewDisplayApplied();
            return;
        }

        outputState = SmoothNightColorOutputState(target, smooth: true);
        displayApplied = TryApplyNightColorDisplay(activeMonitors, outputState);
        RememberNightColorPreviewDisplayApplied(displayApplied);
    }

    private bool ReserveNightColorPreviewApplySlot(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_lastNightColorPreviewDisplayApplied is not null && now < _nextNightColorPreviewApplyAt)
            {
                return false;
            }

            _nextNightColorPreviewApplyAt = now + NightColorPreviewMinimumApplyInterval;
            return true;
        }
    }

    private bool LastNightColorPreviewDisplayApplied()
    {
        lock (_gate)
        {
            return _lastNightColorPreviewDisplayApplied == true;
        }
    }

    private NightColorState LastNightColorOutputStateOr(NightColorState fallback)
    {
        lock (_gate)
        {
            return _lastNightColorOutputState ?? fallback;
        }
    }

    private void RememberNightColorPreviewDisplayApplied(bool displayApplied)
    {
        lock (_gate)
        {
            _lastNightColorPreviewDisplayApplied = displayApplied;
        }
    }

    private void ResetNightColorPreviewApplyState()
    {
        _nextNightColorPreviewApplyAt = DateTimeOffset.MinValue;
        _lastNightColorPreviewDisplayApplied = null;
    }

    private void RestoreNightColorDisplayState()
    {
        MonitorPipeline[] pipelines;
        LumaBrakeSettings settings;

        lock (_gate)
        {
            pipelines = _pipelines.Values.ToArray();
            settings = Settings;
        }

        if (pipelines.Length == 0)
        {
            TryClearNightColorDisplay();
            return;
        }

        var foregroundApplication = CurrentForegroundApplication();
        var activeMonitors = pipelines.Select(pipeline => pipeline.Monitor).ToArray();
        var now = DateTimeOffset.UtcNow;
        var nightColor = ApplicationExcludesNightColor(settings, foregroundApplication)
            ? NightColorState.Disabled with { Reason = "application-rule" }
            : NightColorEngine.Evaluate(
                settings.NightColor,
                now.ToLocalTime(),
                foregroundApplication,
                foregroundApplication.IsFullScreen);
        ResetNightColorOutputState(nightColor);

        if (!TryApplyNightColorDisplay(activeMonitors, nightColor))
        {
            TryClearNightColorDisplay();
        }

        ClearNightColorPreviewOverlay(pipelines);
        PublishSnapshot();
    }

    private bool TryApplyNightColorPreviewOverlay(
        IReadOnlyList<MonitorPipeline> pipelines,
        LumaBrakeSettings settings,
        NightColorState state)
    {
        try
        {
            foreach (var pipeline in pipelines)
            {
                var activeRequests = new List<EffectRequest>();
                AddNightColorRequest(activeRequests, state, DateTimeOffset.UtcNow);
                var effect = state.Enabled
                    ? _composer.Compose(activeRequests, settings.UserMaximumOpacity, state.Tint)
                    : FinalEffectState.Clear;
                _overlay.SetEffect(pipeline.Monitor.StableId, effect);
            }

            return state.Enabled;
        }
        catch (Exception)
        {
            RecoverFromTransientLoopFailure();
            return false;
        }
    }

    private void ClearNightColorPreviewOverlay(IReadOnlyList<MonitorPipeline> pipelines)
    {
        try
        {
            foreach (var pipeline in pipelines)
            {
                _overlay.SetEffect(pipeline.Monitor.StableId, FinalEffectState.Clear);
            }
        }
        catch (Exception)
        {
            RecoverFromTransientLoopFailure();
        }
    }

    private NightColorState SmoothNightColorOutputState(NightColorState target, bool smooth)
    {
        lock (_gate)
        {
            if (!smooth || !target.Enabled)
            {
                _lastNightColorOutputState = target.Enabled ? target : null;
                return target;
            }

            var previous = _lastNightColorOutputState ?? NightColorState.Disabled;
            var smoothed = target with
            {
                TemperatureKelvin = StepInt(previous.TemperatureKelvin, target.TemperatureKelvin, NightColorMaximumKelvinStep),
                Brightness = StepDouble(previous.Brightness, target.Brightness, NightColorMaximumBrightnessStep),
                NightProgress = StepDouble(previous.NightProgress, target.NightProgress, NightColorMaximumProgressStep),
                DimmingOpacity = StepDouble(previous.DimmingOpacity, target.DimmingOpacity, NightColorMaximumDimmingOpacityStep),
                Tint = StepTint(previous.Tint, target.Tint)
            };

            if (NightColorStateClose(smoothed, target))
            {
                smoothed = target;
            }

            _lastNightColorOutputState = smoothed;
            return smoothed;
        }
    }

    private void ResetNightColorOutputState(NightColorState state)
    {
        lock (_gate)
        {
            _lastNightColorOutputState = state.Enabled ? state : null;
        }
    }

    private void RefreshMonitorsIfDue(DateTimeOffset now)
    {
        if (now < _nextMonitorRefreshAt)
        {
            return;
        }

        _nextMonitorRefreshAt = now + _monitorRefreshInterval;
        RefreshMonitorsCore(forcePublish: false);
    }

    private void AnalyzeAndRender()
    {
        MonitorPipeline[] pipelines;
        int renderGeneration;
        LumaBrakeSettings settings;
        NightColorState? nightColorPreviewOverride;

        lock (_gate)
        {
            pipelines = _pipelines.Values.ToArray();
            renderGeneration = _renderGeneration;
            settings = Settings;
            nightColorPreviewOverride = _nightColorPreviewOverride;
        }

        if (pipelines.Length == 0)
        {
            TryHideAll();
            PublishSnapshot();
            return;
        }

        var activeMonitors = pipelines.Select(pipeline => pipeline.Monitor).ToArray();
        var now = DateTimeOffset.UtcNow;
        if (nightColorPreviewOverride is not null)
        {
            RenderNightColorPreviewOnly(
                pipelines,
                activeMonitors,
                nightColorPreviewOverride,
                renderGeneration,
                now,
                runtimeEnabledForThisRender: true);
            return;
        }

        var foregroundApplication = CurrentForegroundApplication();
        var applicationExcludesAutomaticEffects = ApplicationExcludesAutomaticEffects(settings, foregroundApplication);
        var nightColor = ApplicationExcludesNightColor(settings, foregroundApplication)
            ? NightColorState.Disabled with { Reason = "application-rule" }
            : NightColorEngine.Evaluate(
                settings.NightColor,
                now.ToLocalTime(),
                foregroundApplication,
                foregroundApplication.IsFullScreen);
        var outputNightColor = SmoothNightColorOutputState(
            nightColor,
            settings.NightColor.TransitionMode == NightColorTransitionMode.Smooth);
        var nightColorDisplayApplied = TryApplyNightColorDisplay(activeMonitors, outputNightColor);
        var needsFocusedMonitor = !applicationExcludesAutomaticEffects &&
            (settings.FocusDimming.Enabled || settings.InactiveMonitor.Enabled);
        var focusedMonitorStableId = needsFocusedMonitor
            ? FocusedMonitorStableId(activeMonitors)
            : null;
        var cursorMonitorStableId = !applicationExcludesAutomaticEffects && settings.InactiveMonitor.Enabled
            ? CursorMonitorStableId(activeMonitors)
            : null;

        var renderUpdates = new List<PipelineRenderUpdate>(pipelines.Length);
        var renderUpdatesGate = new object();

        if (pipelines.Length == 1)
        {
            if (TryAnalyzePipeline(
                pipelines[0],
                settings,
                applicationExcludesAutomaticEffects,
                foregroundApplication,
                focusedMonitorStableId,
                cursorMonitorStableId,
                outputNightColor,
                nightColorDisplayApplied,
                renderGeneration,
                out var update))
            {
                renderUpdates.Add(update);
            }
        }
        else
        {
            Parallel.ForEach(pipelines, pipeline =>
            {
                if (!TryAnalyzePipeline(
                    pipeline,
                    settings,
                    applicationExcludesAutomaticEffects,
                    foregroundApplication,
                    focusedMonitorStableId,
                    cursorMonitorStableId,
                    outputNightColor,
                    nightColorDisplayApplied,
                    renderGeneration,
                    out var update))
                {
                    return;
                }

                lock (renderUpdatesGate)
                {
                    renderUpdates.Add(update);
                }
            });
        }

        foreach (var update in renderUpdates)
        {
            _overlay.SetEffect(update.MonitorStableId, update.Effect);
            if (!IsRenderGenerationCurrent(renderGeneration))
            {
                _overlay.SetEffect(update.MonitorStableId, FinalEffectState.Clear);
            }
        }

        PublishSnapshot();
    }

    private void RenderNightColorOnly()
    {
        MonitorPipeline[] pipelines;
        int renderGeneration;
        LumaBrakeSettings settings;
        NightColorState? nightColorPreviewOverride;

        lock (_gate)
        {
            pipelines = _pipelines.Values.ToArray();
            renderGeneration = _renderGeneration;
            settings = Settings;
            nightColorPreviewOverride = _nightColorPreviewOverride;
        }

        if (pipelines.Length == 0)
        {
            TryClearNightColorDisplay();
            TryHideAll();
            PublishSnapshot();
            return;
        }

        var foregroundApplication = CurrentForegroundApplication();
        var activeMonitors = pipelines.Select(pipeline => pipeline.Monitor).ToArray();
        var now = DateTimeOffset.UtcNow;
        if (nightColorPreviewOverride is not null)
        {
            RenderNightColorPreviewOnly(
                pipelines,
                activeMonitors,
                nightColorPreviewOverride,
                renderGeneration,
                now,
                runtimeEnabledForThisRender: false);
            return;
        }

        var nightColor = ApplicationExcludesNightColor(settings, foregroundApplication)
            ? NightColorState.Disabled with { Reason = "application-rule" }
            : NightColorEngine.Evaluate(
                settings.NightColor,
                now.ToLocalTime(),
                foregroundApplication,
                foregroundApplication.IsFullScreen);
        var outputNightColor = SmoothNightColorOutputState(
            nightColor,
            settings.NightColor.TransitionMode == NightColorTransitionMode.Smooth);
        var nightColorDisplayApplied = TryApplyNightColorDisplay(activeMonitors, outputNightColor);
        var renderUpdates = new List<PipelineRenderUpdate>(pipelines.Length);

        foreach (var pipeline in pipelines)
        {
            if (TryComposeNightColorOnlyRenderUpdate(
                    pipeline,
                    outputNightColor,
                    nightColorDisplayApplied,
                    renderGeneration,
                    now,
                    runtimeEnabledForThisRender: false,
                    out var update))
            {
                renderUpdates.Add(update);
            }
        }

        foreach (var update in renderUpdates)
        {
            _overlay.SetEffect(update.MonitorStableId, update.Effect);
            if (!IsNightColorOnlyRenderGenerationCurrent(renderGeneration))
            {
                _overlay.SetEffect(update.MonitorStableId, FinalEffectState.Clear);
            }
        }

        PublishSnapshot();
    }

    private void RenderNightColorPreviewOnly(
        IReadOnlyList<MonitorPipeline> pipelines,
        IReadOnlyList<MonitorDescriptor> activeMonitors,
        NightColorState previewState,
        int renderGeneration,
        DateTimeOffset now,
        bool runtimeEnabledForThisRender)
    {
        PrepareNightColorPreviewFrame(
            activeMonitors,
            previewState,
            now,
            out var outputNightColor,
            out var nightColorDisplayApplied);
        var renderUpdates = new List<PipelineRenderUpdate>(pipelines.Count);

        foreach (var pipeline in pipelines)
        {
            if (TryComposeNightColorOnlyRenderUpdate(
                    pipeline,
                    outputNightColor,
                    nightColorDisplayApplied,
                    renderGeneration,
                    now,
                    runtimeEnabledForThisRender,
                    out var update))
            {
                renderUpdates.Add(update);
            }
        }

        foreach (var update in renderUpdates)
        {
            _overlay.SetEffect(update.MonitorStableId, update.Effect);
            if (!IsNightColorRenderGenerationCurrent(renderGeneration, runtimeEnabledForThisRender))
            {
                _overlay.SetEffect(update.MonitorStableId, FinalEffectState.Clear);
            }
        }

        PublishSnapshot();
    }

    private bool TryAnalyzePipeline(
        MonitorPipeline pipeline,
        LumaBrakeSettings settings,
        bool applicationExcludesAutomaticEffects,
        ForegroundApplicationInfo foregroundApplication,
        string? focusedMonitorStableId,
        string? cursorMonitorStableId,
        NightColorState nightColor,
        bool nightColorDisplayApplied,
        int renderGeneration,
        out PipelineRenderUpdate update)
    {
        update = default;
        if (!TryAnalyzeFrame(
            pipeline,
            settings,
            foregroundApplication,
            nightColorDisplayApplied,
            out var captured,
            out var analysis))
        {
            return TryComposeNonAnalysisRenderUpdate(
                pipeline,
                settings,
                foregroundApplication,
                nightColor,
                nightColorDisplayApplied,
                renderGeneration,
                out update);
        }

        var nearEmptyFrame = FrameSamplingQuality.IsNearEmpty(captured.BgraPixels, captured.Metadata);
        var nextNearEmptyFrameStreak = nearEmptyFrame ? pipeline.NearEmptyFrameStreak + 1 : 0;
        var captureAnomalyActive = nextNearEmptyFrameStreak >= CaptureAnomalyActivationFrames;
        var automaticEffectsSuppressed = AutomaticEffectsSuppressed(
            pipeline.Monitor,
            applicationExcludesAutomaticEffects,
            captureAnomalyActive);
        var focusDimmingSuppressed = FocusDimmingSuppressed(
            pipeline.Monitor,
            applicationExcludesAutomaticEffects);
        var spike = !automaticEffectsSuppressed && settings.SpikeGuard.Enabled ? pipeline.Detector.Observe(analysis) : null;
        var flash = !automaticEffectsSuppressed && settings.FlashGuard.Enabled ? pipeline.FlashDetector.Observe(analysis) : null;
        var staticShade = automaticEffectsSuppressed ? null : pipeline.StaticShadeDetector.Observe(analysis);
        var adaptive = automaticEffectsSuppressed ? null : pipeline.AdaptiveDetector.Observe(analysis);
        var monitorHasInteraction = IsInteractionMonitor(
            pipeline.Monitor,
            focusedMonitorStableId,
            cursorMonitorStableId);
        var inactive = automaticEffectsSuppressed
            ? null
            : ObserveInactiveMonitor(pipeline, analysis, monitorHasInteraction);
        FinalEffectState final;

        lock (_gate)
        {
            if (!_pipelines.TryGetValue(pipeline.Monitor.StableId, out var current))
            {
                return false;
            }

            if (!Settings.Enabled || renderGeneration != _renderGeneration)
            {
                ClearPipelineEffectState(current);
                final = FinalEffectState.Clear;
            }
            else
            {
                current.FramesAnalyzed++;
                current.CaptureFailureStreak = 0;
                current.LastAverage = analysis.AverageLuminance;
                current.LastP95 = analysis.Percentile95Luminance;
                if (nearEmptyFrame)
                {
                    current.NearEmptyFrameStreak = nextNearEmptyFrameStreak;
                    current.CaptureAnomalyEvents++;
                    current.LastCaptureAnomalyAt = analysis.Metadata.CapturedAt;
                }
                else
                {
                    current.NearEmptyFrameStreak = 0;
                }

                if (spike is not null)
                {
                    current.SpikeEvents++;
                    current.Envelope.Add(new EffectRequest(
                        EffectReason.BrightnessSpike,
                        spike.RecommendedOpacity,
                        spike.Timestamp,
                        settings.SpikeGuard.Hold,
                        settings.SpikeGuard.Release,
                        spike.AffectedRegions));
                }

                if (flash is not null)
                {
                    current.FlashEvents++;
                    if (flash.IsRedDominant)
                    {
                        current.RedFlashEvents++;
                    }

                    var useRegionalFlashMask = UsesRegionalFlashMask(flash);
                    if (useRegionalFlashMask)
                    {
                        current.Envelope.Add(new EffectRequest(
                            EffectReason.RepetitiveFlash,
                            LocalizedFlashGlobalFloorOpacity(flash.RecommendedOpacity),
                            flash.Timestamp,
                            settings.FlashGuard.Hold,
                            settings.FlashGuard.Release));
                    }

                    var flashRegions = useRegionalFlashMask
                        ? flash.AffectedRegions
                        : Array.Empty<EffectRegion>();
                    current.Envelope.Add(new EffectRequest(
                        EffectReason.RepetitiveFlash,
                        flash.RecommendedOpacity,
                        flash.Timestamp,
                        settings.FlashGuard.Hold,
                        settings.FlashGuard.Release,
                        flashRegions));
                }

                if (staticShade is not null)
                {
                    if (staticShade.RecommendedOpacity <= 0)
                    {
                        current.Envelope.ReleaseRegional(
                            EffectReason.StaticShade,
                            staticShade.Timestamp,
                            RegionalClearRelease(settings.StaticShade.Release));
                    }
                    else
                    {
                        current.Envelope.Add(new EffectRequest(
                            EffectReason.StaticShade,
                            staticShade.RecommendedOpacity,
                            staticShade.Timestamp,
                            settings.StaticShade.Hold,
                            settings.StaticShade.Release,
                            staticShade.AffectedRegions));
                    }
                }

                if (adaptive is not null)
                {
                    current.Envelope.Add(new EffectRequest(
                        EffectReason.AdaptiveContent,
                        adaptive.RecommendedOpacity,
                        adaptive.Timestamp,
                        settings.AdaptiveContent.Hold,
                        settings.AdaptiveContent.Release));
                }

                if (inactive is not null)
                {
                    current.Envelope.Add(new EffectRequest(
                        EffectReason.InactiveMonitor,
                        inactive.RecommendedOpacity,
                        inactive.Timestamp,
                        settings.InactiveMonitor.Hold,
                        settings.InactiveMonitor.Release));
                }

                if (monitorHasInteraction)
                {
                    current.Envelope.Remove(EffectReason.InactiveMonitor);
                }

                if (captureAnomalyActive)
                {
                    current.RebuildDetectors(settings);
                }

                if (automaticEffectsSuppressed)
                {
                    if (focusDimmingSuppressed)
                    {
                        current.Envelope.Clear();
                    }
                    else
                    {
                        ClearContentAnalysisEffects(current.Envelope);
                    }
                }

                var now = DateTimeOffset.UtcNow;
                var focusDimming = settings.FocusDimming.Clamp();
                if (!focusDimmingSuppressed &&
                    AppliesFocusDimming(current.Monitor, focusedMonitorStableId, focusDimming))
                {
                    current.Envelope.Add(new EffectRequest(
                        EffectReason.FocusDimming,
                        focusDimming.Opacity,
                        now,
                        focusDimming.Hold,
                        focusDimming.Release));
                }

                var activeRequests = current.Envelope.ActiveAt(now).ToList();
                var activeSchedule = settings.Schedules.ActiveAt(now.ToLocalTime())?.Clamp();
                AddManualScheduleAndNightColorRequests(
                    activeRequests,
                    settings,
                    current.Monitor,
                    activeSchedule,
                    nightColor,
                    nightColorDisplayApplied,
                    now);

                var monitorTint = settings.MonitorTints.FindForMonitor(current.Monitor)?.Tint;
                final = _composer.Compose(
                    activeRequests,
                    settings.UserMaximumOpacity,
                    EffectiveTint(settings.Tint, monitorTint, activeSchedule, nightColor, !nightColorDisplayApplied));
                current.LastOpacity = final.GlobalOpacity;
                current.LastReason = final.GlobalReason;
                current.LastRegionalMaskCount = final.RegionalMasks.Count;
                current.LastRegionalMaskMaxOpacity = final.RegionalMasks.Select(mask => mask.Opacity).DefaultIfEmpty(0).Max();
                current.LastRegionalMasks = final.RegionalMasks.ToArray();
            }
        }

        update = new PipelineRenderUpdate(pipeline.Monitor.StableId, final);
        return true;
    }

    private bool TryComposeNonAnalysisRenderUpdate(
        MonitorPipeline pipeline,
        LumaBrakeSettings settings,
        ForegroundApplicationInfo foregroundApplication,
        NightColorState nightColor,
        bool nightColorDisplayApplied,
        int renderGeneration,
        out PipelineRenderUpdate update)
    {
        update = default;
        var now = DateTimeOffset.UtcNow;
        if (nightColorDisplayApplied || !HasActiveNightColorFallback(settings, foregroundApplication, now))
        {
            return false;
        }

        FinalEffectState final;

        lock (_gate)
        {
            if (!_pipelines.TryGetValue(pipeline.Monitor.StableId, out var current))
            {
                return false;
            }

            if (!Settings.Enabled || renderGeneration != _renderGeneration)
            {
                ClearPipelineEffectState(current);
                final = FinalEffectState.Clear;
            }
            else
            {
                current.Envelope.Clear();
                var activeRequests = new List<EffectRequest>();
                AddNightColorRequest(activeRequests, nightColor, now);

                var monitorTint = settings.MonitorTints.FindForMonitor(current.Monitor)?.Tint;
                final = _composer.Compose(
                    activeRequests,
                    settings.UserMaximumOpacity,
                    EffectiveTint(settings.Tint, monitorTint, activeSchedule: null, nightColor, useNightColorOverlayTint: true));
                current.LastOpacity = final.GlobalOpacity;
                current.LastReason = final.GlobalReason;
                current.LastRegionalMaskCount = final.RegionalMasks.Count;
                current.LastRegionalMaskMaxOpacity = final.RegionalMasks.Select(mask => mask.Opacity).DefaultIfEmpty(0).Max();
                current.LastRegionalMasks = final.RegionalMasks.ToArray();
            }
        }

        update = new PipelineRenderUpdate(pipeline.Monitor.StableId, final);
        return true;
    }

    private bool TryComposeNightColorOnlyRenderUpdate(
        MonitorPipeline pipeline,
        NightColorState nightColor,
        bool nightColorDisplayApplied,
        int renderGeneration,
        DateTimeOffset now,
        bool runtimeEnabledForThisRender,
        out PipelineRenderUpdate update)
    {
        update = default;
        FinalEffectState final;

        lock (_gate)
        {
            if (!_pipelines.TryGetValue(pipeline.Monitor.StableId, out var current))
            {
                return false;
            }

            ClearPipelineEffectState(current);
            if (Settings.Enabled != runtimeEnabledForThisRender || renderGeneration != _renderGeneration)
            {
                final = FinalEffectState.Clear;
            }
            else
            {
                var activeRequests = new List<EffectRequest>();
                if (!nightColorDisplayApplied)
                {
                    AddNightColorRequest(activeRequests, nightColor, now);
                }

                final = _composer.Compose(
                    activeRequests,
                    Settings.UserMaximumOpacity,
                    nightColorDisplayApplied ? TintEffect.Disabled : nightColor.Tint);
                current.LastOpacity = final.GlobalOpacity;
                current.LastReason = final.GlobalReason;
                current.LastRegionalMaskCount = final.RegionalMasks.Count;
                current.LastRegionalMaskMaxOpacity = final.RegionalMasks.Select(mask => mask.Opacity).DefaultIfEmpty(0).Max();
                current.LastRegionalMasks = final.RegionalMasks.ToArray();
            }
        }

        update = new PipelineRenderUpdate(pipeline.Monitor.StableId, final);
        return true;
    }

    private bool TryAnalyzeFrame(
        MonitorPipeline pipeline,
        LumaBrakeSettings settings,
        ForegroundApplicationInfo foregroundApplication,
        bool nightColorDisplayApplied,
        out CapturedFrame captured,
        out FrameAnalysisResult analysis)
    {
        try
        {
            captured = _capture.Capture(pipeline.Monitor);
        }
        catch (Exception exception) when (IsRecoverableFrameSamplingFailure(exception))
        {
            HandleFrameSamplingFailure(
                pipeline,
                settings,
                foregroundApplication,
                nightColorDisplayApplied,
                DateTimeOffset.UtcNow);
            captured = null!;
            analysis = null!;
            return false;
        }

        analysis = _analyzer.Analyze(captured.BgraPixels, captured.Metadata);
        return true;
    }

    private void HandleFrameSamplingFailure(
        MonitorPipeline pipeline,
        LumaBrakeSettings settings,
        ForegroundApplicationInfo foregroundApplication,
        bool nightColorDisplayApplied,
        DateTimeOffset now)
    {
        var failedAt = now;
        var shouldClearOverlay = false;
        var hasNightColorFallback = !nightColorDisplayApplied &&
            HasActiveNightColorFallback(settings, foregroundApplication, now);

        lock (_gate)
        {
            if (_pipelines.TryGetValue(pipeline.Monitor.StableId, out var current))
            {
                var nextFailureStreak = current.CaptureFailureStreak + 1;
                current.CaptureFailureEvents++;
                current.LastCaptureFailureAt = failedAt;
                if (nextFailureStreak >= CaptureFailureClearThreshold)
                {
                    current.RebuildDetectors(Settings);
                    if (hasNightColorFallback)
                    {
                        ClearContentAnalysisEffects(current.Envelope);
                        current.LastRegionalMaskCount = 0;
                        current.LastRegionalMaskMaxOpacity = 0;
                        current.LastRegionalMasks = Array.Empty<RegionalEffectMask>();
                    }
                    else
                    {
                        ClearPipelineEffectState(current);
                        shouldClearOverlay = true;
                    }
                }

                current.CaptureFailureStreak = nextFailureStreak;
            }
        }

        if (shouldClearOverlay)
        {
            try
            {
                _overlay.SetEffect(pipeline.Monitor.StableId, FinalEffectState.Clear);
            }
            catch (Exception)
            {
                RecoverFromTransientLoopFailure();
            }
        }
    }

    private string? FocusedMonitorStableId(IReadOnlyList<MonitorDescriptor> monitors)
    {
        try
        {
            var focusedMonitorStableId = _focusedMonitorProvider.FocusedMonitorStableId(monitors);
            var validFocusedMonitorStableId = MatchingMonitorStableId(monitors, focusedMonitorStableId);
            if (validFocusedMonitorStableId is not null)
            {
                _lastKnownFocusedMonitorStableId = validFocusedMonitorStableId;
                _lastKnownFocusedMonitorAt = DateTimeOffset.UtcNow;
                return validFocusedMonitorStableId;
            }

            if (!string.IsNullOrWhiteSpace(focusedMonitorStableId))
            {
                ClearLastKnownFocusedMonitor();
                return null;
            }

            return LastKnownFocusedMonitorStableId(monitors, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (IsRecoverableFocusLookupFailure(exception))
        {
            return LastKnownFocusedMonitorStableId(monitors, DateTimeOffset.UtcNow);
        }
    }

    private string? LastKnownFocusedMonitorStableId(IReadOnlyList<MonitorDescriptor> monitors, DateTimeOffset now)
    {
        if (_lastKnownFocusedMonitorStableId is null || _lastKnownFocusedMonitorAt is null)
        {
            return null;
        }

        if (now - _lastKnownFocusedMonitorAt.Value > FocusedMonitorLookupGracePeriod)
        {
            _lastKnownFocusedMonitorStableId = null;
            _lastKnownFocusedMonitorAt = null;
            return null;
        }

        var validStableId = MatchingMonitorStableId(monitors, _lastKnownFocusedMonitorStableId);
        if (validStableId is null)
        {
            ClearLastKnownFocusedMonitor();
        }

        return validStableId;
    }

    private void ClearLastKnownFocusedMonitor()
    {
        _lastKnownFocusedMonitorStableId = null;
        _lastKnownFocusedMonitorAt = null;
    }

    private static string? MatchingMonitorStableId(IReadOnlyList<MonitorDescriptor> monitors, string? stableId) =>
        string.IsNullOrWhiteSpace(stableId)
            ? null
            : monitors
                .FirstOrDefault(monitor => string.Equals(monitor.StableId, stableId, StringComparison.OrdinalIgnoreCase))
                ?.StableId;

    private static bool IsRecoverableFocusLookupFailure(Exception exception) =>
        exception is InvalidOperationException or ObjectDisposedException or COMException or DllNotFoundException or EntryPointNotFoundException;

    private string? CursorMonitorStableId(IReadOnlyList<MonitorDescriptor> monitors)
    {
        try
        {
            var cursorMonitorStableId = _cursorMonitorProvider.CursorMonitorStableId(monitors);
            return monitors.Any(monitor => string.Equals(monitor.StableId, cursorMonitorStableId, StringComparison.OrdinalIgnoreCase))
                ? cursorMonitorStableId
                : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private ForegroundApplicationInfo CurrentForegroundApplication()
    {
        try
        {
            return _foregroundApplicationProvider.Current();
        }
        catch (InvalidOperationException)
        {
            return ForegroundApplicationInfo.Unknown;
        }
    }

    private static bool ApplicationExcludesAutomaticEffects(
        LumaBrakeSettings settings,
        ForegroundApplicationInfo foregroundApplication)
    {
        var applicationRules = settings.ApplicationRules;
        if (!applicationRules.Enabled || applicationRules.Entries is null or { Length: 0 })
        {
            return false;
        }

        return applicationRules.ExcludesAutomaticEffects(foregroundApplication);
    }

    private static bool ApplicationExcludesNightColor(
        LumaBrakeSettings settings,
        ForegroundApplicationInfo foregroundApplication)
    {
        var applicationRules = settings.ApplicationRules;
        if (!applicationRules.Enabled || applicationRules.Entries is null or { Length: 0 })
        {
            return false;
        }

        return applicationRules.ExcludesNightColor(foregroundApplication);
    }

    private static bool HasActiveNightColorFallback(
        LumaBrakeSettings settings,
        ForegroundApplicationInfo foregroundApplication,
        DateTimeOffset now) =>
        !ApplicationExcludesNightColor(settings, foregroundApplication) &&
        NightColorEngine.Evaluate(
            settings.NightColor,
            now.ToLocalTime(),
            foregroundApplication,
            foregroundApplication.IsFullScreen).Enabled;

    private static bool AutomaticEffectsSuppressed(
        MonitorDescriptor monitor,
        bool applicationExcludesAutomaticEffects,
        bool captureAnomaly) =>
        applicationExcludesAutomaticEffects ||
        captureAnomaly ||
        monitor.AdvancedColor.RequiresConservativeHandling;

    private static bool FocusDimmingSuppressed(
        MonitorDescriptor monitor,
        bool applicationExcludesAutomaticEffects) =>
        applicationExcludesAutomaticEffects ||
        monitor.AdvancedColor.RequiresConservativeHandling;

    private static TimeSpan RegionalClearRelease(TimeSpan configuredRelease)
    {
        if (configuredRelease <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return configuredRelease <= MaximumRegionalClearRelease
            ? configuredRelease
            : MaximumRegionalClearRelease;
    }

    private static void ClearContentAnalysisEffects(EffectEnvelope envelope)
    {
        envelope.Remove(EffectReason.BrightnessSpike);
        envelope.Remove(EffectReason.RepetitiveFlash);
        envelope.Remove(EffectReason.StaticShade);
        envelope.Remove(EffectReason.AdaptiveContent);
        envelope.Remove(EffectReason.InactiveMonitor);
    }

    private static bool AppliesFocusDimming(
        MonitorDescriptor monitor,
        string? focusedMonitorStableId,
        FocusDimmingOptions options) =>
        options.Enabled &&
        options.Opacity > 0 &&
        !string.IsNullOrWhiteSpace(focusedMonitorStableId) &&
        !string.Equals(monitor.StableId, focusedMonitorStableId, StringComparison.OrdinalIgnoreCase);

    private static bool IsInteractionMonitor(
        MonitorDescriptor monitor,
        string? focusedMonitorStableId,
        string? cursorMonitorStableId) =>
        MatchesStableId(monitor, focusedMonitorStableId) ||
        MatchesStableId(monitor, cursorMonitorStableId);

    private static bool MatchesStableId(MonitorDescriptor monitor, string? stableId) =>
        !string.IsNullOrWhiteSpace(stableId) &&
        string.Equals(monitor.StableId, stableId, StringComparison.OrdinalIgnoreCase);

    private static void AddManualScheduleAndNightColorRequests(
        List<EffectRequest> activeRequests,
        LumaBrakeSettings settings,
        MonitorDescriptor monitor,
        ScheduledEffect? activeSchedule,
        NightColorState nightColor,
        bool nightColorDisplayApplied,
        DateTimeOffset now)
    {
        var manualDimmingOpacity = EffectiveManualDimmingOpacity(settings, monitor);
        if (manualDimmingOpacity > 0)
        {
            activeRequests.Add(new EffectRequest(
                EffectReason.Manual,
                manualDimmingOpacity,
                now,
                TimeSpan.FromMilliseconds(100),
                TimeSpan.Zero));
        }

        if (activeSchedule is not null && activeSchedule.ManualDimmingOpacity > 0)
        {
            activeRequests.Add(new EffectRequest(
                EffectReason.Scheduled,
                ScheduledAdditionalOpacity(activeSchedule.ManualDimmingOpacity, manualDimmingOpacity),
                now,
                TimeSpan.FromMilliseconds(100),
                TimeSpan.Zero));
        }

        if (!nightColorDisplayApplied && nightColor.DimmingOpacity > 0)
        {
            AddNightColorRequest(
                activeRequests,
                nightColor,
                now,
                ScheduledAdditionalOpacity(nightColor.DimmingOpacity, manualDimmingOpacity));
        }
    }

    private static void AddNightColorRequest(
        List<EffectRequest> activeRequests,
        NightColorState nightColor,
        DateTimeOffset now,
        double? effectiveOpacity = null)
    {
        var opacity = Math.Clamp(effectiveOpacity ?? nightColor.DimmingOpacity, 0, 0.90);
        if (opacity <= 0)
        {
            return;
        }

        activeRequests.Add(new EffectRequest(
            EffectReason.NightColor,
            opacity,
            now,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.Zero));
    }

    private static InactiveMonitorEvent? ObserveInactiveMonitor(
        MonitorPipeline pipeline,
        FrameAnalysisResult analysis,
        bool monitorHasInteraction)
    {
        if (pipeline.Monitor.IsPrimary || monitorHasInteraction)
        {
            pipeline.InactiveDetector.Reset();
            return null;
        }

        return pipeline.InactiveDetector.Observe(analysis);
    }

    private bool IsRenderGenerationCurrent(int generation)
    {
        lock (_gate)
        {
            return Settings.Enabled && generation == _renderGeneration;
        }
    }

    private bool IsNightColorOnlyRenderGenerationCurrent(int generation)
    {
        return IsNightColorRenderGenerationCurrent(generation, runtimeEnabledForThisRender: false);
    }

    private bool IsNightColorRenderGenerationCurrent(int generation, bool runtimeEnabledForThisRender)
    {
        lock (_gate)
        {
            return Settings.Enabled == runtimeEnabledForThisRender && generation == _renderGeneration;
        }
    }

    private static void ClearPipelineEffectState(MonitorPipeline pipeline)
    {
        pipeline.Envelope.Clear();
        pipeline.LastOpacity = 0;
        pipeline.LastReason = EffectReason.None;
        pipeline.LastRegionalMaskCount = 0;
        pipeline.LastRegionalMaskMaxOpacity = 0;
        pipeline.LastRegionalMasks = Array.Empty<RegionalEffectMask>();
        pipeline.NearEmptyFrameStreak = 0;
        pipeline.CaptureFailureStreak = 0;
    }

    private static bool IsRecoverableFrameSamplingFailure(Exception exception) =>
        exception is FrameCaptureDeviceLostException or InvalidOperationException or ObjectDisposedException or COMException;

    private static bool IsRecoverableMonitorEnumerationFailure(Exception exception) =>
        exception is InvalidOperationException or ObjectDisposedException or COMException or DllNotFoundException or EntryPointNotFoundException;

    private static double ScheduledAdditionalOpacity(double scheduledOpacity, double manualOpacity)
    {
        var target = Math.Clamp(scheduledOpacity, 0, 0.90);
        var current = Math.Clamp(manualOpacity, 0, 0.90);
        if (target <= current)
        {
            return 0;
        }

        return Math.Clamp(1 - ((1 - target) / (1 - current)), 0, 0.90);
    }

    private static double EffectiveManualDimmingOpacity(LumaBrakeSettings settings, MonitorDescriptor monitor)
    {
        var monitorOverride = settings.MonitorManualDimming.FindForMonitor(monitor);
        return Math.Clamp(monitorOverride?.Opacity ?? settings.ManualDimmingOpacity, 0, 0.90);
    }

    private static TintEffect EffectiveTint(
        TintOptions baseTint,
        TintOptions? monitorTint,
        ScheduledEffect? activeSchedule,
        NightColorState nightColor,
        bool useNightColorOverlayTint)
    {
        if (useNightColorOverlayTint && nightColor.Tint.Enabled)
        {
            return nightColor.Tint;
        }

        var scheduledTint = activeSchedule?.Tint.ToEffect();
        if (scheduledTint is { Enabled: true })
        {
            return scheduledTint;
        }

        return monitorTint is null ? baseTint.ToEffect() : monitorTint.ToEffect();
    }

    private static bool UsesRegionalFlashMask(FlashEvent flash) =>
        flash.AffectedTileRatio < RegionalFlashFallbackRatio &&
        flash.AffectedRegions.Count > 0;

    private static double LocalizedFlashGlobalFloorOpacity(double recommendedOpacity) =>
        Math.Clamp(
            Math.Max(LocalizedFlashGlobalFloorMinimumOpacity, recommendedOpacity * LocalizedFlashGlobalFloorScale),
            0,
            LocalizedFlashGlobalFloorMaximumOpacity);

    private static int StepInt(int current, int target, int maximumStep) =>
        current == target
            ? target
            : current + Math.Clamp(target - current, -maximumStep, maximumStep);

    private static double StepDouble(double current, double target, double maximumStep)
    {
        if (Math.Abs(target - current) <= maximumStep)
        {
            return target;
        }

        return current + (Math.Sign(target - current) * maximumStep);
    }

    private static TintEffect StepTint(TintEffect current, TintEffect target)
    {
        var targetTint = target.Clamp();
        if (!targetTint.Enabled)
        {
            return TintEffect.Disabled;
        }

        var currentOpacity = current.Enabled ? current.Clamp().Opacity : 0;
        var opacity = StepDouble(currentOpacity, targetTint.Opacity, NightColorMaximumTintOpacityStep);
        return opacity <= 0.001
            ? TintEffect.Disabled
            : targetTint with { Opacity = opacity };
    }

    private static bool NightColorStateClose(NightColorState current, NightColorState target) =>
        current.TemperatureKelvin == target.TemperatureKelvin &&
        Math.Abs(current.Brightness - target.Brightness) <= 0.0001 &&
        Math.Abs(current.NightProgress - target.NightProgress) <= 0.0001 &&
        Math.Abs(current.DimmingOpacity - target.DimmingOpacity) <= 0.0001 &&
        current.Tint.Enabled == target.Tint.Enabled &&
        Math.Abs(current.Tint.Opacity - target.Tint.Opacity) <= 0.0001;

    private void PublishSnapshot()
    {
        SnapshotUpdated?.Invoke(this, Snapshot());
    }

    private static bool MonitorTopologyEquals(
        IReadOnlyList<MonitorDescriptor> current,
        IReadOnlyList<MonitorDescriptor> next) =>
        current.Count == next.Count &&
        current.Zip(next).All(pair => MonitorRuntimeEquals(pair.First, pair.Second));

    private static bool MonitorRuntimeEquals(MonitorDescriptor current, MonitorDescriptor next) =>
        string.Equals(current.StableId, next.StableId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(current.DeviceName, next.DeviceName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(current.DisplayName, next.DisplayName, StringComparison.Ordinal) &&
        current.IsPrimary == next.IsPrimary &&
        current.IsActive == next.IsActive &&
        current.Bounds == next.Bounds &&
        current.WorkArea == next.WorkArea &&
        Math.Abs(current.DpiScale - next.DpiScale) < 0.001 &&
        current.AdvancedColor == next.AdvancedColor;

    private static TimeSpan NormalizeAnalysisInterval(TimeSpan? interval) =>
        interval is { } value && value > TimeSpan.Zero
            ? value
            : DefaultAnalysisInterval;

    private static MonitorPipelineSnapshot ToSnapshot(MonitorPipeline pipeline) =>
        new MonitorPipelineSnapshot(
            pipeline.Monitor.StableId,
            pipeline.Monitor.DeviceName,
            pipeline.Monitor.DisplayName,
            pipeline.Monitor.IsPrimary,
            pipeline.LastAverage,
            pipeline.LastP95,
            pipeline.LastOpacity,
            pipeline.LastReason,
            pipeline.FramesAnalyzed,
            pipeline.CaptureAnomalyEvents,
            pipeline.LastCaptureAnomalyAt,
            pipeline.IsCaptureAnomalyActive,
            pipeline.CaptureFailureEvents,
            pipeline.LastCaptureFailureAt,
            pipeline.IsCaptureFailureActive,
            pipeline.SpikeEvents,
            pipeline.FlashEvents,
            pipeline.RedFlashEvents,
            pipeline.LastRegionalMaskCount,
            pipeline.LastRegionalMaskMaxOpacity,
            pipeline.Monitor.AdvancedColor)
        {
            RegionalMasks = pipeline.LastRegionalMasks
        };

    private readonly record struct PipelineRenderUpdate(string MonitorStableId, FinalEffectState Effect);

    private sealed class MonitorPipeline
    {
        public MonitorPipeline(MonitorDescriptor monitor, LumaBrakeSettings settings)
        {
            Monitor = monitor;
            Detector = new BrightnessSpikeDetector(settings.SpikeGuard);
            FlashDetector = new RepetitiveFlashDetector(settings.FlashGuard);
            StaticShadeDetector = new StaticShadeDetector(settings.StaticShade);
            AdaptiveDetector = new AdaptiveContentDimmingDetector(settings.AdaptiveContent);
            InactiveDetector = new InactiveMonitorDimmingDetector(settings.InactiveMonitor);
        }

        public MonitorDescriptor Monitor { get; set; }

        public BrightnessSpikeDetector Detector { get; private set; }

        public RepetitiveFlashDetector FlashDetector { get; private set; }

        public StaticShadeDetector StaticShadeDetector { get; private set; }

        public AdaptiveContentDimmingDetector AdaptiveDetector { get; private set; }

        public InactiveMonitorDimmingDetector InactiveDetector { get; private set; }

        public EffectEnvelope Envelope { get; } = new();

        public long FramesAnalyzed { get; set; }

        public long CaptureAnomalyEvents { get; set; }

        public DateTimeOffset? LastCaptureAnomalyAt { get; set; }

        public int NearEmptyFrameStreak { get; set; }

        public bool IsCaptureAnomalyActive => NearEmptyFrameStreak >= CaptureAnomalyActivationFrames;

        public long CaptureFailureEvents { get; set; }

        public DateTimeOffset? LastCaptureFailureAt { get; set; }

        public int CaptureFailureStreak { get; set; }

        public bool IsCaptureFailureActive => CaptureFailureStreak > 0;

        public long SpikeEvents { get; set; }

        public long FlashEvents { get; set; }

        public long RedFlashEvents { get; set; }

        public double LastAverage { get; set; }

        public double LastP95 { get; set; }

        public double LastOpacity { get; set; }

        public EffectReason LastReason { get; set; } = EffectReason.None;

        public int LastRegionalMaskCount { get; set; }

        public double LastRegionalMaskMaxOpacity { get; set; }

        public IReadOnlyList<RegionalEffectMask> LastRegionalMasks { get; set; } = Array.Empty<RegionalEffectMask>();

        public void RebuildDetectors(LumaBrakeSettings settings)
        {
            Detector = new BrightnessSpikeDetector(settings.SpikeGuard);
            FlashDetector = new RepetitiveFlashDetector(settings.FlashGuard);
            StaticShadeDetector = new StaticShadeDetector(settings.StaticShade);
            AdaptiveDetector = new AdaptiveContentDimmingDetector(settings.AdaptiveContent);
            InactiveDetector = new InactiveMonitorDimmingDetector(settings.InactiveMonitor);
        }

        public void ResetRuntimeState(LumaBrakeSettings settings)
        {
            RebuildDetectors(settings);
            Envelope.Clear();
            LastAverage = 0;
            LastP95 = 0;
            LastOpacity = 0;
            LastReason = EffectReason.None;
            LastRegionalMaskCount = 0;
            LastRegionalMaskMaxOpacity = 0;
            LastRegionalMasks = Array.Empty<RegionalEffectMask>();
            NearEmptyFrameStreak = 0;
            CaptureFailureStreak = 0;
        }
    }

    private sealed class NoFocusedMonitorProvider : IFocusedMonitorProvider
    {
        public static readonly NoFocusedMonitorProvider Instance = new();

        public string? FocusedMonitorStableId(IReadOnlyList<MonitorDescriptor> monitors) => null;
    }

    private sealed class NoCursorMonitorProvider : ICursorMonitorProvider
    {
        public static readonly NoCursorMonitorProvider Instance = new();

        public string? CursorMonitorStableId(IReadOnlyList<MonitorDescriptor> monitors) => null;
    }

    private sealed class NoForegroundApplicationProvider : IForegroundApplicationProvider
    {
        public static readonly NoForegroundApplicationProvider Instance = new();

        public ForegroundApplicationInfo Current() => ForegroundApplicationInfo.Unknown;
    }

    private sealed class NoNightColorDisplayController : INightColorDisplayController
    {
        public static readonly NoNightColorDisplayController Instance = new();

        public bool Apply(IReadOnlyList<MonitorDescriptor> monitors, NightColorState state) => false;

        public void Clear()
        {
        }

        public void Dispose()
        {
        }
    }
}
