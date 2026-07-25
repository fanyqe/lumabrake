namespace LumaBrake.Domain.Analysis;

public sealed record AdaptiveContentEvent(
    DateTimeOffset Timestamp,
    double BrightnessScore,
    TimeSpan BrightDuration,
    double RecommendedOpacity);
