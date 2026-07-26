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
        Assert.Empty(state.RegionalMasks);
        Assert.False(state.Tint.Enabled);
    }

    [Fact]
    public void Compose_CarriesTintWithoutAddingDimming()
    {
        var state = new EffectComposer().Compose(
            [],
            userMaximumOpacity: 0.90,
            new TintEffect(true, 255, 214, 170, 0.18));

        Assert.Equal(0, state.GlobalOpacity);
        Assert.Equal(EffectReason.None, state.GlobalReason);
        Assert.True(state.Tint.Enabled);
        Assert.Equal(0.18, state.Tint.Opacity, precision: 12);
    }

    [Fact]
    public void Compose_KeepsRegionalMasksOutOfGlobalOpacity()
    {
        var composer = new EffectComposer();
        var now = DateTimeOffset.UnixEpoch;

        var state = composer.Compose(
            [
                new EffectRequest(
                    EffectReason.RepetitiveFlash,
                    0.70,
                    now,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    [new EffectRegion(0, 0, 0.25, 0.25)])
            ],
            userMaximumOpacity: 0.40);

        Assert.Equal(0, state.GlobalOpacity);
        Assert.Equal(EffectReason.RepetitiveFlash, state.GlobalReason);
        var mask = Assert.Single(state.RegionalMasks);
        Assert.Equal(0.40, mask.Opacity, precision: 12);
        Assert.Equal(new EffectRegion(0, 0, 0.25, 0.25), mask.Region);
    }

    [Fact]
    public void Compose_CapsCombinedGlobalAndRegionalOpacity()
    {
        var composer = new EffectComposer();
        var now = DateTimeOffset.UnixEpoch;

        var state = composer.Compose(
            [
                new EffectRequest(EffectReason.Manual, 0.40, now, TimeSpan.Zero, TimeSpan.Zero),
                new EffectRequest(
                    EffectReason.RepetitiveFlash,
                    0.70,
                    now,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    [new EffectRegion(0, 0, 0.50, 0.50)])
            ],
            userMaximumOpacity: 0.60);

        var regionalOpacity = Assert.Single(state.RegionalMasks).Opacity;
        var combinedRegionalAreaOpacity = 1 - ((1 - state.GlobalOpacity) * (1 - regionalOpacity));

        Assert.Equal(0.40, state.GlobalOpacity, precision: 12);
        Assert.Equal(0.60, combinedRegionalAreaOpacity, precision: 12);
    }

    [Fact]
    public void Compose_TreatsScheduledDimmingAsLowerPriorityThanAdaptiveContent()
    {
        var now = DateTimeOffset.UnixEpoch;

        var state = new EffectComposer().Compose(
            [
                new EffectRequest(EffectReason.Scheduled, 0.20, now, TimeSpan.Zero, TimeSpan.Zero),
                new EffectRequest(EffectReason.AdaptiveContent, 0.10, now, TimeSpan.Zero, TimeSpan.Zero)
            ],
            userMaximumOpacity: 0.90);

        Assert.Equal(0.28, state.GlobalOpacity, precision: 3);
        Assert.Equal(EffectReason.AdaptiveContent, state.GlobalReason);
    }
}
