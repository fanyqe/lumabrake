using LumaBrake.Domain.Effects;

namespace LumaBrake.Domain.Analysis;

public sealed record SpikeEvent(
    DateTimeOffset Timestamp,
    double BaselineLuminance,
    double CurrentLuminance,
    double Delta,
    double Ratio,
    double Severity,
    double RecommendedOpacity,
    IReadOnlyList<EffectRegion>? AffectedRegions = null)
{
    public IReadOnlyList<EffectRegion> AffectedRegions { get; init; } =
        AffectedRegions?.ToArray() ?? Array.Empty<EffectRegion>();

    public bool IsRegional => AffectedRegions.Count > 0;
}
