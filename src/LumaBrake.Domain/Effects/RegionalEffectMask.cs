namespace LumaBrake.Domain.Effects;

public sealed record RegionalEffectMask(
    EffectReason Reason,
    double Opacity,
    EffectRegion Region);
