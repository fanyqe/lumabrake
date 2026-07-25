using LumaBrake.Analysis.Detection;
using LumaBrake.Analysis.Frames;
using LumaBrake.Capture;
using LumaBrake.Diagnostics;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Monitors;
using LumaBrake.Effects.Composition;
using LumaBrake.Persistence;
using LumaBrake.Rendering;

namespace LumaBrake.Core;

public sealed class LumaBrakeController : IDisposable
{
    private readonly IFrameCapture _capture;
    private readonly IOverlayRenderer _overlay;
    private readonly Bgra32FrameAnalyzer _analyzer = new();
    private readonly EffectComposer _composer = new();
    private readonly IMonitorCatalog _monitorCatalog;
    private readonly Dictionary<string, MonitorPipeline> _pipelines = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _gate = new();
    private Task? _loop;
    private bool _disposed;
    private IReadOnlyList<MonitorDescriptor> _monitors = Array.Empty<MonitorDescriptor>();
    private string _monitorStatus = "Monitor list has not been refreshed.";

    public LumaBrakeController(IFrameCapture capture, IOverlayRenderer overlay, LumaBrakeSettings settings, IMonitorCatalog monitorCatalog)
    {
        _capture = capture;
        _overlay = overlay;
        Settings = settings;
        _monitorCatalog = monitorCatalog;
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
            EmergencyDisable();
        }
    }

    public void SetEnabled(bool enabled)
    {
        Settings = Settings with { Enabled = enabled };
        if (!enabled)
        {
            EmergencyDisable();
        }
    }

    public void SetUserMaximumOpacity(double maximumOpacity)
    {
        Settings = Settings with { UserMaximumOpacity = Math.Clamp(maximumOpacity, 0.05, 0.92) };
    }

    public void EmergencyDisable()
    {
        lock (_gate)
        {
            foreach (var pipeline in _pipelines.Values)
            {
                pipeline.Envelope.Clear();
                pipeline.LastOpacity = 0;
                pipeline.LastReason = EffectReason.None;
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

        lock (_gate)
        {
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

        foreach (var pipeline in pipelines)
        {
            var captured = _capture.Capture(pipeline.Monitor);
            var analysis = _analyzer.Analyze(captured.BgraPixels, captured.Metadata);
            var spike = pipeline.Detector.Observe(analysis);
            double opacity;

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

                var final = _composer.Compose(current.Envelope.ActiveAt(DateTimeOffset.UtcNow), Settings.UserMaximumOpacity);
                current.LastOpacity = final.GlobalOpacity;
                current.LastReason = final.GlobalReason;
                opacity = final.GlobalOpacity;
            }

            _overlay.SetOpacity(pipeline.Monitor.StableId, opacity);
        }

        PublishSnapshot();
    }

    private void PublishSnapshot()
    {
        SnapshotUpdated?.Invoke(this, Snapshot());
    }

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
            pipeline.SpikeEvents);

    private sealed class MonitorPipeline
    {
        public MonitorPipeline(MonitorDescriptor monitor, LumaBrakeSettings settings)
        {
            Monitor = monitor;
            Detector = new BrightnessSpikeDetector(settings.SpikeGuard);
        }

        public MonitorDescriptor Monitor { get; set; }

        public BrightnessSpikeDetector Detector { get; }

        public EffectEnvelope Envelope { get; } = new();

        public long FramesAnalyzed { get; set; }

        public long SpikeEvents { get; set; }

        public double LastAverage { get; set; }

        public double LastP95 { get; set; }

        public double LastOpacity { get; set; }

        public EffectReason LastReason { get; set; } = EffectReason.None;
    }
}
