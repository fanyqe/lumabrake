using LumaBrake.Analysis.Detection;
using LumaBrake.Analysis.Frames;
using LumaBrake.Capture;
using LumaBrake.Diagnostics;
using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Frames;
using LumaBrake.Domain.Monitors;
using LumaBrake.Domain.Schedules;
using LumaBrake.Effects.Composition;
using LumaBrake.Persistence;
using LumaBrake.Rendering;

namespace LumaBrake.Core;

public sealed class LumaBrakeController : IDisposable
{
    private const double RegionalFlashFallbackRatio = 0.35;
    private static readonly TimeSpan DefaultMonitorRefreshInterval = TimeSpan.FromSeconds(2);

    private readonly IFrameCapture _capture;
    private readonly IOverlayRenderer _overlay;
    private readonly Bgra32FrameAnalyzer _analyzer = new();
    private readonly EffectComposer _composer = new();
    private readonly IMonitorCatalog _monitorCatalog;
    private readonly IFocusedMonitorProvider _focusedMonitorProvider;
    private readonly TimeSpan _monitorRefreshInterval;
    private readonly Dictionary<string, MonitorPipeline> _pipelines = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _gate = new();
    private Task? _loop;
    private bool _disposed;
    private DateTimeOffset _nextMonitorRefreshAt = DateTimeOffset.MinValue;
    private IReadOnlyList<MonitorDescriptor> _monitors = Array.Empty<MonitorDescriptor>();
    private string _monitorStatus = "Monitor list has not been refreshed.";

    public LumaBrakeController(
        IFrameCapture capture,
        IOverlayRenderer overlay,
        LumaBrakeSettings settings,
        IMonitorCatalog monitorCatalog,
        IFocusedMonitorProvider? focusedMonitorProvider = null,
        TimeSpan? monitorRefreshInterval = null)
    {
        _capture = capture;
        _overlay = overlay;
        Settings = settings;
        _monitorCatalog = monitorCatalog;
        _focusedMonitorProvider = focusedMonitorProvider ?? NoFocusedMonitorProvider.Instance;
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
        _overlay.HideAll();
        _loop ??= Task.Run(() => RunAsync(_shutdown.Token));
    }

    public void ToggleEnabled()
    {
        Settings = Settings with { Enabled = !Settings.Enabled };
        if (!Settings.Enabled)
        {
            ClearActiveEffects(resetManualDimming: false);
        }
    }

    public void SetEnabled(bool enabled)
    {
        Settings = Settings with { Enabled = enabled };
        if (!enabled)
        {
            ClearActiveEffects(resetManualDimming: false);
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

    public void EmergencyDisable()
    {
        ClearActiveEffects(resetManualDimming: true, disableRuntime: true);
    }

    private void ClearActiveEffects(bool resetManualDimming, bool disableRuntime = false)
    {
        lock (_gate)
        {
            if (disableRuntime)
            {
                Settings = Settings with { Enabled = false };
            }

            if (resetManualDimming)
            {
                Settings = Settings with { ManualDimmingOpacity = 0 };
            }

            foreach (var pipeline in _pipelines.Values)
            {
                pipeline.Envelope.Clear();
                pipeline.LastOpacity = 0;
                pipeline.LastReason = EffectReason.None;
                pipeline.LastRegionalMaskCount = 0;
                pipeline.LastRegionalMaskMaxOpacity = 0;
            }
        }

        _overlay.HideAll();
        PublishSnapshot();
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
                pipelines.Sum(pipeline => pipeline.FramesAnalyzed),
                pipelines.Sum(pipeline => pipeline.SpikeEvents),
                pipelines.Sum(pipeline => pipeline.FlashEvents),
                pipelines.Sum(pipeline => pipeline.RedFlashEvents),
                pipelines.Sum(pipeline => pipeline.LastRegionalMaskCount),
                pipelines.Select(pipeline => pipeline.LastRegionalMaskMaxOpacity).DefaultIfEmpty(0).Max(),
                monitorSnapshots);
        }
    }

    public IReadOnlyList<MonitorDescriptor> Monitors()
    {
        lock (_gate)
        {
            return _monitors.ToArray();
        }
    }

    public void RefreshMonitors()
    {
        RefreshMonitorsCore(forcePublish: true);
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
        catch (InvalidOperationException)
        {
            monitors = Array.Empty<MonitorDescriptor>();
            monitorStatus = "Monitor detection failed.";
        }

        var changed = false;
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
            }

            foreach (var monitor in monitors)
            {
                if (_pipelines.TryGetValue(monitor.StableId, out var pipeline))
                {
                    pipeline.Monitor = monitor;
                    continue;
                }

                _pipelines[monitor.StableId] = new MonitorPipeline(monitor, Settings);
            }
        }

        _overlay.SyncMonitors(monitors);
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
        _capture.Dispose();
        _shutdown.Dispose();
        _disposed = true;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(66));

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                RefreshMonitorsIfDue(DateTimeOffset.UtcNow);

                if (Settings.Enabled)
                {
                    AnalyzeAndRender();
                }
                else
                {
                    _overlay.HideAll();
                }
            }
            catch (Exception)
            {
                EmergencyDisable();
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
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

        lock (_gate)
        {
            pipelines = _pipelines.Values.ToArray();
        }

        if (pipelines.Length == 0)
        {
            _overlay.HideAll();
            PublishSnapshot();
            return;
        }

        var focusedMonitorStableId = FocusedMonitorStableId(pipelines.Select(pipeline => pipeline.Monitor).ToArray());

        foreach (var pipeline in pipelines)
        {
            var captured = _capture.Capture(pipeline.Monitor);
            var analysis = _analyzer.Analyze(captured.BgraPixels, captured.Metadata);
            var spike = pipeline.Detector.Observe(analysis);
            var flash = pipeline.FlashDetector.Observe(analysis);
            var staticShade = pipeline.StaticShadeDetector.Observe(analysis);
            var adaptive = pipeline.AdaptiveDetector.Observe(analysis);
            var inactive = ObserveInactiveMonitor(pipeline, analysis);
            FinalEffectState final;

            lock (_gate)
            {
                if (!_pipelines.TryGetValue(pipeline.Monitor.StableId, out var current))
                {
                    continue;
                }

                current.FramesAnalyzed++;
                current.LastAverage = analysis.AverageLuminance;
                current.LastP95 = analysis.Percentile95Luminance;

                if (spike is not null)
                {
                    current.SpikeEvents++;
                    current.Envelope.Add(new EffectRequest(
                        EffectReason.BrightnessSpike,
                        spike.RecommendedOpacity,
                        spike.Timestamp,
                        Settings.SpikeGuard.Hold,
                        Settings.SpikeGuard.Release));
                }

                if (flash is not null)
                {
                    current.FlashEvents++;
                    if (flash.IsRedDominant)
                    {
                        current.RedFlashEvents++;
                    }

                    var flashRegions = flash.AffectedTileRatio < RegionalFlashFallbackRatio
                        ? flash.AffectedRegions
                        : Array.Empty<EffectRegion>();
                    current.Envelope.Add(new EffectRequest(
                        EffectReason.RepetitiveFlash,
                        flash.RecommendedOpacity,
                        flash.Timestamp,
                        Settings.FlashGuard.Hold,
                        Settings.FlashGuard.Release,
                        flashRegions));
                }

                if (staticShade is not null)
                {
                    current.Envelope.Add(new EffectRequest(
                        EffectReason.StaticShade,
                        staticShade.RecommendedOpacity,
                        staticShade.Timestamp,
                        Settings.StaticShade.Hold,
                        Settings.StaticShade.Release,
                        staticShade.AffectedRegions));
                }

                if (adaptive is not null)
                {
                    current.Envelope.Add(new EffectRequest(
                        EffectReason.AdaptiveContent,
                        adaptive.RecommendedOpacity,
                        adaptive.Timestamp,
                        Settings.AdaptiveContent.Hold,
                        Settings.AdaptiveContent.Release));
                }

                if (inactive is not null)
                {
                    current.Envelope.Add(new EffectRequest(
                        EffectReason.InactiveMonitor,
                        inactive.RecommendedOpacity,
                        inactive.Timestamp,
                        Settings.InactiveMonitor.Hold,
                        Settings.InactiveMonitor.Release));
                }

                var now = DateTimeOffset.UtcNow;
                var focusDimming = Settings.FocusDimming.Clamp();
                if (AppliesFocusDimming(current.Monitor, focusedMonitorStableId, focusDimming))
                {
                    current.Envelope.Add(new EffectRequest(
                        EffectReason.FocusDimming,
                        focusDimming.Opacity,
                        now,
                        focusDimming.Hold,
                        focusDimming.Release));
                }

                var activeRequests = current.Envelope.ActiveAt(now).ToList();
                var activeSchedule = Settings.Schedules.ActiveAt(now.ToLocalTime())?.Clamp();
                if (Settings.ManualDimmingOpacity > 0)
                {
                    activeRequests.Add(new EffectRequest(
                        EffectReason.Manual,
                        Settings.ManualDimmingOpacity,
                        now,
                        TimeSpan.FromMilliseconds(100),
                        TimeSpan.Zero));
                }

                if (activeSchedule is not null && activeSchedule.ManualDimmingOpacity > 0)
                {
                    activeRequests.Add(new EffectRequest(
                        EffectReason.Scheduled,
                        ScheduledAdditionalOpacity(activeSchedule.ManualDimmingOpacity, Settings.ManualDimmingOpacity),
                        now,
                        TimeSpan.FromMilliseconds(100),
                        TimeSpan.Zero));
                }

                var monitorTint = Settings.MonitorTints.FindForMonitor(current.Monitor)?.Tint;
                final = _composer.Compose(activeRequests, Settings.UserMaximumOpacity, EffectiveTint(Settings.Tint, monitorTint, activeSchedule));
                current.LastOpacity = final.GlobalOpacity;
                current.LastReason = final.GlobalReason;
                current.LastRegionalMaskCount = final.RegionalMasks.Count;
                current.LastRegionalMaskMaxOpacity = final.RegionalMasks.Select(mask => mask.Opacity).DefaultIfEmpty(0).Max();
            }

            _overlay.SetEffect(pipeline.Monitor.StableId, final);
        }

        PublishSnapshot();
    }

    private string? FocusedMonitorStableId(IReadOnlyList<MonitorDescriptor> monitors)
    {
        if (!Settings.FocusDimming.Enabled)
        {
            return null;
        }

        try
        {
            var focusedMonitorStableId = _focusedMonitorProvider.FocusedMonitorStableId(monitors);
            return monitors.Any(monitor => string.Equals(monitor.StableId, focusedMonitorStableId, StringComparison.OrdinalIgnoreCase))
                ? focusedMonitorStableId
                : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool AppliesFocusDimming(
        MonitorDescriptor monitor,
        string? focusedMonitorStableId,
        FocusDimmingOptions options) =>
        options.Enabled &&
        options.Opacity > 0 &&
        !string.IsNullOrWhiteSpace(focusedMonitorStableId) &&
        !string.Equals(monitor.StableId, focusedMonitorStableId, StringComparison.OrdinalIgnoreCase);

    private static InactiveMonitorEvent? ObserveInactiveMonitor(MonitorPipeline pipeline, FrameAnalysisResult analysis)
    {
        if (pipeline.Monitor.IsPrimary)
        {
            pipeline.InactiveDetector.Reset();
            return null;
        }

        return pipeline.InactiveDetector.Observe(analysis);
    }

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

    private static TintEffect EffectiveTint(TintOptions baseTint, TintOptions? monitorTint, ScheduledEffect? activeSchedule)
    {
        var scheduledTint = activeSchedule?.Tint.ToEffect();
        if (scheduledTint is { Enabled: true })
        {
            return scheduledTint;
        }

        return monitorTint is null ? baseTint.ToEffect() : monitorTint.ToEffect();
    }

    private void PublishSnapshot()
    {
        SnapshotUpdated?.Invoke(this, Snapshot());
    }

    private static bool MonitorTopologyEquals(
        IReadOnlyList<MonitorDescriptor> current,
        IReadOnlyList<MonitorDescriptor> next) =>
        current.Count == next.Count &&
        current.SequenceEqual(next);

    private static MonitorPipelineSnapshot ToSnapshot(MonitorPipeline pipeline) =>
        new(
            pipeline.Monitor.StableId,
            pipeline.Monitor.DeviceName,
            pipeline.Monitor.DisplayName,
            pipeline.Monitor.IsPrimary,
            pipeline.LastAverage,
            pipeline.LastP95,
            pipeline.LastOpacity,
            pipeline.LastReason,
            pipeline.FramesAnalyzed,
            pipeline.SpikeEvents,
            pipeline.FlashEvents,
            pipeline.RedFlashEvents,
            pipeline.LastRegionalMaskCount,
            pipeline.LastRegionalMaskMaxOpacity);

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

        public BrightnessSpikeDetector Detector { get; }

        public RepetitiveFlashDetector FlashDetector { get; }

        public StaticShadeDetector StaticShadeDetector { get; }

        public AdaptiveContentDimmingDetector AdaptiveDetector { get; }

        public InactiveMonitorDimmingDetector InactiveDetector { get; }

        public EffectEnvelope Envelope { get; } = new();

        public long FramesAnalyzed { get; set; }

        public long SpikeEvents { get; set; }

        public long FlashEvents { get; set; }

        public long RedFlashEvents { get; set; }

        public double LastAverage { get; set; }

        public double LastP95 { get; set; }

        public double LastOpacity { get; set; }

        public EffectReason LastReason { get; set; } = EffectReason.None;

        public int LastRegionalMaskCount { get; set; }

        public double LastRegionalMaskMaxOpacity { get; set; }
    }

    private sealed class NoFocusedMonitorProvider : IFocusedMonitorProvider
    {
        public static readonly NoFocusedMonitorProvider Instance = new();

        public string? FocusedMonitorStableId(IReadOnlyList<MonitorDescriptor> monitors) => null;
    }
}
