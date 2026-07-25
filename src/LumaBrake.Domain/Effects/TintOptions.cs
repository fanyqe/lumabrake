namespace LumaBrake.Domain.Effects;

public sealed record TintOptions
{
    public bool Enabled { get; init; }

    public byte Red { get; init; } = 255;

    public byte Green { get; init; } = 214;

    public byte Blue { get; init; } = 170;

    public double Opacity { get; init; } = 0.12;

    public TintEffect ToEffect() =>
        Enabled
            ? new TintEffect(true, Red, Green, Blue, Opacity).Clamp()
            : TintEffect.Disabled;
}
