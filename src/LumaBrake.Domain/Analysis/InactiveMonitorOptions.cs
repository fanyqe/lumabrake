namespace LumaBrake.Domain.Analysis;

public sealed record InactiveMonitorOptions
{
    public bool Enabled { get; init; } = true;

    public TimeSpan MinimumInactiveDuration { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan MaximumFrameGap { get; init; } = TimeSpan.FromSeconds(2);

    public double MaximumAverageDelta { get; init; } = 0.012;

    public double MaximumPercentile95Delta { get; init; } = 0.020;

    public double MinimumAverageLuminance { get; init; } = 0.20;

    public double Opacity { get; init; } = 0.22;

    public TimeSpan Hold { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan Release { get; init; } = TimeSpan.FromMilliseconds(650);
}
