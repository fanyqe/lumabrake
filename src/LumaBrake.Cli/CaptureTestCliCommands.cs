using System.Diagnostics;
using System.Globalization;
using LumaBrake.Analysis.Frames;
using LumaBrake.Capture;
using LumaBrake.Domain.Frames;
using LumaBrake.Domain.Monitors;
using LumaBrake.Platform.Windows;

namespace LumaBrake.Cli;

internal static class CaptureTestCliCommands
{
    public static int Run(IReadOnlyList<string> args)
    {
        if (args.Count < 2 || string.Equals(args[1], "primary", StringComparison.OrdinalIgnoreCase))
        {
            return CaptureMonitors([PrimaryMonitor.Get()]);
        }

        if (string.Equals(args[1], "all", StringComparison.OrdinalIgnoreCase))
        {
            var monitors = new WindowsMonitorEnumerator().GetMonitors();
            if (monitors.Count == 0)
            {
                Console.Error.WriteLine("No active Windows monitor was found.");
                return 1;
            }

            return CaptureMonitors(monitors);
        }

        Console.Error.WriteLine("Usage: lumabrake capture-test [primary|all]");
        return 2;
    }

    private static int CaptureMonitors(IReadOnlyList<MonitorDescriptor> monitors)
    {
        using var capture = new GdiMonitorCapture();
        var analyzer = new Bgra32FrameAnalyzer();
        var failures = 0;

        for (var index = 0; index < monitors.Count; index++)
        {
            var monitor = monitors[index];
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var frame = capture.Capture(monitor);
                var result = analyzer.Analyze(frame.BgraPixels, frame.Metadata);
                stopwatch.Stop();

                WriteResult(index + 1, monitors.Count, monitor, frame, result, stopwatch.Elapsed);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                failures++;
                Console.Error.WriteLine($"Capture failed for {monitor.DeviceName}: {ex.Message}");
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static void WriteResult(
        int index,
        int count,
        MonitorDescriptor monitor,
        CapturedFrame frame,
        FrameAnalysisResult result,
        TimeSpan elapsed)
    {
        var metadata = frame.Metadata;
        if (count > 1)
        {
            Console.WriteLine($"Monitor {index.ToString(CultureInfo.InvariantCulture)} of {count.ToString(CultureInfo.InvariantCulture)}");
        }

        Console.WriteLine($"Source monitor: {metadata.Source}");
        Console.WriteLine($"Source bounds: {monitor.Bounds.Left},{monitor.Bounds.Top} {monitor.Bounds.Width}x{monitor.Bounds.Height}");
        Console.WriteLine($"Captured frame: {metadata.Width}x{metadata.Height}");
        Console.WriteLine($"Average luminance: {result.AverageLuminance.ToString("0.000", CultureInfo.InvariantCulture)}");
        Console.WriteLine($"95th percentile luminance: {result.Percentile95Luminance.ToString("0.000", CultureInfo.InvariantCulture)}");
        Console.WriteLine(FrameSamplingQuality.IsNearEmpty(frame.BgraPixels, metadata)
            ? "Sampling quality: limited; protected content or exclusive full-screen capture may be blocking the frame"
            : "Sampling quality: available");
        Console.WriteLine($"Capture and analysis elapsed: {elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)} ms");
    }
}
