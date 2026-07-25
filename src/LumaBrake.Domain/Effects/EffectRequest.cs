namespace LumaBrake.Domain.Effects;

public sealed record EffectRequest
{
    public EffectRequest(
        EffectReason reason,
        double opacity,
        DateTimeOffset requestedAt,
        TimeSpan hold,
        TimeSpan release,
        IReadOnlyList<EffectRegion>? regions = null)
    {
        Reason = reason;
        Opacity = opacity;
        RequestedAt = requestedAt;
        Hold = hold;
        Release = release;
        Regions = regions?.ToArray() ?? Array.Empty<EffectRegion>();
    }

    public EffectReason Reason { get; init; }

    public double Opacity { get; init; }

    public DateTimeOffset RequestedAt { get; init; }

    public TimeSpan Hold { get; init; }

    public TimeSpan Release { get; init; }

    public IReadOnlyList<EffectRegion> Regions { get; init; }

    public bool IsRegional => Regions.Count > 0;
}
