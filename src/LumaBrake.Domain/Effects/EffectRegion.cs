namespace LumaBrake.Domain.Effects;

public sealed record EffectRegion(double X, double Y, double Width, double Height)
{
    public double Area => Math.Clamp(Width, 0, 1) * Math.Clamp(Height, 0, 1);

    public EffectRegion Clamp()
    {
        var x = Math.Clamp(X, 0, 1);
        var y = Math.Clamp(Y, 0, 1);
        var right = Math.Clamp(X + Width, 0, 1);
        var bottom = Math.Clamp(Y + Height, 0, 1);

        return new EffectRegion(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }
}
