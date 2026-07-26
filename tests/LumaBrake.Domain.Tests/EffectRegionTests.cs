using LumaBrake.Domain.Effects;

namespace LumaBrake.Domain.Tests;

public sealed class EffectRegionTests
{
    [Fact]
    public void Pad_ExpandsRegionInsideNormalizedBounds()
    {
        var region = new EffectRegion(0.10, 0.20, 0.30, 0.40).Pad(0.05);

        Assert.Equal(0.05, region.X, precision: 12);
        Assert.Equal(0.15, region.Y, precision: 12);
        Assert.Equal(0.40, region.Width, precision: 12);
        Assert.Equal(0.50, region.Height, precision: 12);
    }

    [Fact]
    public void Union_ReturnsBoundingRegion()
    {
        var union = new EffectRegion(0.10, 0.10, 0.20, 0.20)
            .Union(new EffectRegion(0.25, 0.30, 0.10, 0.20));

        Assert.Equal(0.10, union.X, precision: 12);
        Assert.Equal(0.10, union.Y, precision: 12);
        Assert.Equal(0.25, union.Width, precision: 12);
        Assert.Equal(0.40, union.Height, precision: 12);
    }

    [Fact]
    public void TouchesOrOverlaps_UsesOptionalTolerance()
    {
        var first = new EffectRegion(0, 0, 0.10, 0.10);
        var second = new EffectRegion(0.12, 0, 0.10, 0.10);

        Assert.False(first.TouchesOrOverlaps(second));
        Assert.True(first.TouchesOrOverlaps(second, tolerance: 0.02));
    }
}
