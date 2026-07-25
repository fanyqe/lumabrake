using LumaBrake.Domain.Effects;

namespace LumaBrake.Domain.Analysis;

public sealed record StaticShadeEvent(
    DateTimeOffset Timestamp,
    int AffectedTileCount,
    int TotalTileCount,
    double AffectedTileRatio,
    TimeSpan LongestStaticDuration,
    double RecommendedOpacity,
    IReadOnlyList<EffectRegion> AffectedRegions);
