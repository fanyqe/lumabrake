namespace LumaBrake.Domain.Effects;

public sealed record FinalEffectState(
    double GlobalOpacity,
    EffectReason GlobalReason,
    IReadOnlyList<string> Reasons)
{
    public static FinalEffectState Clear { get; } = new(0, EffectReason.None, Array.Empty<string>());
}
