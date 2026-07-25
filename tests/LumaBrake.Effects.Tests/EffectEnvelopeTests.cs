using LumaBrake.Domain.Effects;
using LumaBrake.Effects.Composition;

namespace LumaBrake.Effects.Tests;

public sealed class EffectEnvelopeTests
{
    [Fact]
    public void ActiveAt_HoldsThenReleasesOpacity()
    {
        var envelope = new EffectEnvelope();
        var now = DateTimeOffset.UnixEpoch;
        envelope.Add(new EffectRequest(
            EffectReason.BrightnessSpike,
            0.50,
            now,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100)));

        Assert.Equal(0.50, envelope.ActiveAt(now.AddMilliseconds(50)).Single().Opacity, precision: 12);
        Assert.Equal(0.25, envelope.ActiveAt(now.AddMilliseconds(150)).Single().Opacity, precision: 12);
        Assert.Empty(envelope.ActiveAt(now.AddMilliseconds(250)));
    }

    [Fact]
    public void Add_ReplacesExistingReason()
    {
        var envelope = new EffectEnvelope();
        var now = DateTimeOffset.UnixEpoch;

        envelope.Add(new EffectRequest(EffectReason.BrightnessSpike, 0.30, now, TimeSpan.FromSeconds(1), TimeSpan.Zero));
        envelope.Add(new EffectRequest(EffectReason.BrightnessSpike, 0.60, now, TimeSpan.FromSeconds(1), TimeSpan.Zero));

        Assert.Equal(0.60, envelope.ActiveAt(now).Single().Opacity, precision: 12);
    }
}
