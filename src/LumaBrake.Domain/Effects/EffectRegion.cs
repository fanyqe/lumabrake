namespace LumaBrake.Domain.Effects;

public sealed record EffectRegion(double X, double Y, double Width, double Height)
{
    public double Area => Math.Clamp(Width, 0, 1) * Math.Clamp(Height, 0, 1);

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public EffectRegion Clamp()
    {
        var x = Math.Clamp(X, 0, 1);
        var y = Math.Clamp(Y, 0, 1);
        var right = Math.Clamp(Right, 0, 1);
        var bottom = Math.Clamp(Bottom, 0, 1);

        return new EffectRegion(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    public EffectRegion Pad(double padding)
    {
        var amount = Math.Max(0, padding);
        return new EffectRegion(X - amount, Y - amount, Width + (amount * 2), Height + (amount * 2)).Clamp();
    }

    public EffectRegion Union(EffectRegion other)
    {
        var first = Clamp();
        var second = other.Clamp();
        var x = Math.Min(first.X, second.X);
        var y = Math.Min(first.Y, second.Y);
        var right = Math.Max(first.Right, second.Right);
        var bottom = Math.Max(first.Bottom, second.Bottom);

        return new EffectRegion(x, y, right - x, bottom - y).Clamp();
    }

    public bool TouchesOrOverlaps(EffectRegion other, double tolerance = 0)
    {
        var first = Clamp();
        var second = other.Clamp();
        var gap = Math.Max(0, tolerance);

        return first.X <= second.Right + gap &&
            first.Right + gap >= second.X &&
            first.Y <= second.Bottom + gap &&
            first.Bottom + gap >= second.Y;
    }
}
