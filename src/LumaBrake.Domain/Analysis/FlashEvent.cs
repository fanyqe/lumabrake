using LumaBrake.Domain.Effects;

namespace LumaBrake.Domain.Analysis;

public sealed record FlashEvent(
    DateTimeOffset Timestamp,
    int AffectedTileCount,
    int TotalTileCount,
    double AffectedTileRatio,
    int MaxTransitionCount,
    double RecommendedOpacity,
    IReadOnlyList<EffectRegion> AffectedRegions);
