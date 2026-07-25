namespace LumaBrake.Domain.Analysis;

public sealed record SpikeEvent(
    DateTimeOffset Timestamp,
    double BaselineLuminance,
    double CurrentLuminance,
    double Delta,
    double Ratio,
    double Severity,
    double RecommendedOpacity);
