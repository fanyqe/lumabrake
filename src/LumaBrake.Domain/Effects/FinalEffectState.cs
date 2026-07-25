namespace LumaBrake.Domain.Effects;

public sealed record FinalEffectState(
    double GlobalOpacity,
    EffectReason GlobalReason,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<RegionalEffectMask> RegionalMasks)
{
    public static FinalEffectState Clear { get; } = new(
        0,
        EffectReason.None,
        Array.Empty<string>(),
        Array.Empty<RegionalEffectMask>());
}
