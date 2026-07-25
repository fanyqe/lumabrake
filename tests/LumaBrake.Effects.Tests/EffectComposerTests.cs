using LumaBrake.Domain.Effects;
using LumaBrake.Effects.Composition;

namespace LumaBrake.Effects.Tests;

public sealed class EffectComposerTests
{
    [Fact]
    public void Compose_UsesMultiplicativeOpacityInsteadOfAddition()
    {
        var composer = new EffectComposer();
        var now = DateTimeOffset.UnixEpoch;

        var state = composer.Compose(
            [
                new EffectRequest(EffectReason.Manual, 0.10, now, TimeSpan.Zero, TimeSpan.Zero),
                new EffectRequest(EffectReason.BrightnessSpike, 0.45, now, TimeSpan.Zero, TimeSpan.Zero)
            ],
            userMaximumOpacity: 0.90);

        Assert.Equal(0.505, state.GlobalOpacity, precision: 3);
        Assert.Equal(EffectReason.Manual, state.GlobalReason);
    }

    [Fact]
    public void Compose_ClampsToUserMaximum()
    {
        var composer = new EffectComposer();
        var now = DateTimeOffset.UnixEpoch;

        var state = composer.Compose(
            [
                new EffectRequest(EffectReason.BrightnessSpike, 0.70, now, TimeSpan.Zero, TimeSpan.Zero),
                new EffectRequest(EffectReason.RepetitiveFlash, 0.70, now, TimeSpan.Zero, TimeSpan.Zero)
            ],
            userMaximumOpacity: 0.60);

        Assert.Equal(0.60, state.GlobalOpacity, precision: 12);
        Assert.Equal(EffectReason.RepetitiveFlash, state.GlobalReason);
    }

    [Fact]
    public void Compose_ReturnsClearStateWithoutRequests()
    {
        var state = new EffectComposer().Compose([], userMaximumOpacity: 0.90);

        Assert.Equal(0, state.GlobalOpacity);
        Assert.Equal(EffectReason.None, state.GlobalReason);
    }
}
