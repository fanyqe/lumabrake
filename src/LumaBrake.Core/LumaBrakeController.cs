using LumaBrake.Analysis.Detection;
using LumaBrake.Analysis.Frames;
using LumaBrake.Capture;
using LumaBrake.Diagnostics;
using LumaBrake.Domain.Effects;
using LumaBrake.Effects.Composition;
using LumaBrake.Persistence;
using LumaBrake.Rendering;

namespace LumaBrake.Core;

public sealed class LumaBrakeController : IDisposable
{
    private readonly IFrameCapture _capture;
    private readonly IOverlayRenderer _overlay;
    private readonly Bgra32FrameAnalyzer _analyzer = new();
    private readonly BrightnessSpikeDetector _detector;
    private readonly EffectEnvelope _envelope = new();
    private readonly EffectComposer _composer = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _gate = new();
    private Task? _loop;
    private bool _disposed;
    private long _framesAnalyzed;
    private long _spikeEvents;
    private double _lastAverage;
    private double _lastP95;
    private double _lastOpacity;
    private EffectReason _lastReason = EffectReason.None;

    public LumaBrakeController(IFrameCapture capture, IOverlayRenderer overlay, LumaBrakeSettings settings)
    {
        _capture = capture;
        _overlay = overlay;
        Settings = settings;
        _detector = new BrightnessSpikeDetector(settings.SpikeGuard);
    }

    public LumaBrakeSettings Settings { get; private set; }

    public event EventHandler<PipelineSnapshot>? SnapshotUpdated;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _overlay.Show();
        _overlay.SetOpacity(0);
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
            _envelope.Clear();
            _lastOpacity = 0;
            _lastReason = EffectReason.None;
        }

        _overlay.HideOverlay();
        PublishSnapshot();
    }

    public PipelineSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new PipelineSnapshot(
                DateTimeOffset.UtcNow,
                Settings.Enabled,
                _lastAverage,
                _lastP95,
                _lastOpacity,
                _lastReason,
                _framesAnalyzed,
                _spikeEvents);
        }
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
                    _overlay.SetOpacity(0);
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
        var captured = _capture.Capture();
        var analysis = _analyzer.Analyze(captured.BgraPixels, captured.Metadata);
        var spike = _detector.Observe(analysis);

        lock (_gate)
        {
            _framesAnalyzed++;
            _lastAverage = analysis.AverageLuminance;
            _lastP95 = analysis.Percentile95Luminance;

            if (spike is not null)
            {
                _spikeEvents++;
                _envelope.Add(new EffectRequest(
                    EffectReason.BrightnessSpike,
                    spike.RecommendedOpacity,
                    spike.Timestamp,
                    Settings.SpikeGuard.Hold,
                    Settings.SpikeGuard.Release));
            }

            var final = _composer.Compose(_envelope.ActiveAt(DateTimeOffset.UtcNow), Settings.UserMaximumOpacity);
            _lastOpacity = final.GlobalOpacity;
            _lastReason = final.GlobalReason;
            _overlay.SetOpacity(final.GlobalOpacity);
        }

        PublishSnapshot();
    }

    private void PublishSnapshot()
    {
        SnapshotUpdated?.Invoke(this, Snapshot());
    }
}
