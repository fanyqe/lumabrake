namespace LumaBrake.Domain.Effects;

public sealed record EffectRequest(
    EffectReason Reason,
    double Opacity,
    DateTimeOffset RequestedAt,
    TimeSpan Hold,
    TimeSpan Release);
