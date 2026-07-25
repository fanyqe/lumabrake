namespace LumaBrake.Domain.Analysis;

public sealed record InactiveMonitorEvent(
    DateTimeOffset Timestamp,
    TimeSpan InactiveDuration,
    double RecommendedOpacity);
