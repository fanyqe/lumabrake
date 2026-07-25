using LumaBrake.Domain.Analysis;

namespace LumaBrake.Domain.Tests;

public sealed class FocusDimmingOptionsTests
{
    [Fact]
    public void Clamp_NormalizesOpacityAndDurations()
    {
        var options = new FocusDimmingOptions
        {
            Opacity = 2,
            Hold = TimeSpan.FromMilliseconds(-1),
            Release = TimeSpan.FromMilliseconds(-1)
        }.Clamp();

        Assert.Equal(0.60, options.Opacity, precision: 12);
        Assert.Equal(TimeSpan.Zero, options.Hold);
        Assert.Equal(TimeSpan.Zero, options.Release);
    }
}
