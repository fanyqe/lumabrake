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

    [Fact]
    public void Add_KeepsGlobalAndRegionalRequestsForSameReason()
    {
        var envelope = new EffectEnvelope();
        var now = DateTimeOffset.UnixEpoch;

        envelope.Add(new EffectRequest(EffectReason.RepetitiveFlash, 0.18, now, TimeSpan.FromSeconds(1), TimeSpan.Zero));
        envelope.Add(new EffectRequest(
            EffectReason.RepetitiveFlash,
            0.60,
            now,
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero,
            [new EffectRegion(0.25, 0.25, 0.25, 0.25)]));

        var active = envelope.ActiveAt(now);

        Assert.Equal(2, active.Count);
        Assert.Contains(active, request => !request.IsRegional && Math.Abs(request.Opacity - 0.18) < 0.001);
        Assert.Contains(active, request => request.IsRegional && Math.Abs(request.Opacity - 0.60) < 0.001);
    }

    [Fact]
    public void Add_ZeroOpacityRegionalRequestClearsOnlyExistingRegionalReason()
    {
        var envelope = new EffectEnvelope();
        var now = DateTimeOffset.UnixEpoch;

        envelope.Add(new EffectRequest(EffectReason.RepetitiveFlash, 0.18, now, TimeSpan.FromSeconds(1), TimeSpan.Zero));
        envelope.Add(new EffectRequest(
            EffectReason.RepetitiveFlash,
            0.30,
            now,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            [new EffectRegion(0.25, 0.25, 0.25, 0.25)]));
        envelope.Add(new EffectRequest(
            EffectReason.RepetitiveFlash,
            0,
            now.AddMilliseconds(50),
            TimeSpan.Zero,
            TimeSpan.Zero,
            [new EffectRegion(0.25, 0.25, 0.25, 0.25)]));

        var active = Assert.Single(envelope.ActiveAt(now.AddMilliseconds(50)));
        Assert.Equal(EffectReason.RepetitiveFlash, active.Reason);
        Assert.False(active.IsRegional);
        Assert.Equal(0.18, active.Opacity, precision: 12);
    }

    [Fact]
    public void Remove_DropsOnlyMatchingReason()
    {
        var envelope = new EffectEnvelope();
        var now = DateTimeOffset.UnixEpoch;

        envelope.Add(new EffectRequest(EffectReason.InactiveMonitor, 0.22, now, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)));
        envelope.Add(new EffectRequest(EffectReason.Manual, 0.10, now, TimeSpan.FromSeconds(30), TimeSpan.Zero));

        envelope.Remove(EffectReason.InactiveMonitor);

        var request = Assert.Single(envelope.ActiveAt(now));
        Assert.Equal(EffectReason.Manual, request.Reason);
        Assert.Equal(0.10, request.Opacity, precision: 12);
    }

    [Fact]
    public void ActiveAt_ReleasesRegionalRequestWithoutDroppingRegion()
    {
        var envelope = new EffectEnvelope();
        var now = DateTimeOffset.UnixEpoch;
        envelope.Add(new EffectRequest(
            EffectReason.RepetitiveFlash,
            0.60,
            now,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100),
            [new EffectRegion(0.25, 0.25, 0.25, 0.25)]));

        var request = envelope.ActiveAt(now.AddMilliseconds(150)).Single();

        Assert.Equal(0.30, request.Opacity, precision: 12);
        Assert.Equal(new EffectRegion(0.25, 0.25, 0.25, 0.25), Assert.Single(request.Regions));
    }

    [Fact]
    public void ReleaseRegional_FadesExistingRegionalRequest()
    {
        var envelope = new EffectEnvelope();
        var now = DateTimeOffset.UnixEpoch;
        var region = new EffectRegion(0.25, 0.25, 0.25, 0.25);
        envelope.Add(new EffectRequest(
            EffectReason.StaticShade,
            0.40,
            now,
            TimeSpan.FromSeconds(10),
            TimeSpan.Zero,
            [region]));

        envelope.ReleaseRegional(EffectReason.StaticShade, now.AddMilliseconds(100), TimeSpan.FromMilliseconds(200));

        var active = envelope.ActiveAt(now.AddMilliseconds(200));

        var request = Assert.Single(active);
        Assert.Equal(EffectReason.StaticShade, request.Reason);
        Assert.Equal(0.20, request.Opacity, precision: 12);
        Assert.Equal(region, Assert.Single(request.Regions));
        Assert.Empty(envelope.ActiveAt(now.AddMilliseconds(350)));
    }

    [Fact]
    public void ReleaseRegional_KeepsGlobalRequestForSameReason()
    {
        var envelope = new EffectEnvelope();
        var now = DateTimeOffset.UnixEpoch;
        envelope.Add(new EffectRequest(
            EffectReason.RepetitiveFlash,
            0.18,
            now,
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero));
        envelope.Add(new EffectRequest(
            EffectReason.RepetitiveFlash,
            0.60,
            now,
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero,
            [new EffectRegion(0.25, 0.25, 0.25, 0.25)]));

        envelope.ReleaseRegional(EffectReason.RepetitiveFlash, now.AddMilliseconds(100), TimeSpan.FromMilliseconds(200));

        var active = envelope.ActiveAt(now.AddMilliseconds(350));

        var request = Assert.Single(active);
        Assert.False(request.IsRegional);
        Assert.Equal(0.18, request.Opacity, precision: 12);
    }
}
