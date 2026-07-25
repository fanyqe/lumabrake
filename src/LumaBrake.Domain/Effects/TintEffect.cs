namespace LumaBrake.Domain.Effects;

public sealed record TintEffect(
    bool Enabled,
    byte Red,
    byte Green,
    byte Blue,
    double Opacity)
{
    public static TintEffect Disabled { get; } = new(false, 255, 255, 255, 0);

    public TintEffect Clamp() =>
        this with { Opacity = Math.Clamp(Opacity, 0, 0.60) };
}
