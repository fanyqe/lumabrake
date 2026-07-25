using LumaBrake.Domain.Effects;

namespace LumaBrake.Effects.Composition;

public sealed class EffectEnvelope
{
    private readonly List<EffectRequest> _requests = [];

    public void Add(EffectRequest request)
    {
        _requests.RemoveAll(existing => existing.Reason == request.Reason);
        _requests.Add(request);
    }

    public IReadOnlyList<EffectRequest> ActiveAt(DateTimeOffset now)
    {
        var active = new List<EffectRequest>();

        foreach (var request in _requests)
        {
            var age = now - request.RequestedAt;
            var holdEnd = request.Hold;
            var end = request.Hold + request.Release;

            if (age < TimeSpan.Zero)
            {
                continue;
            }

            if (age <= holdEnd)
            {
                active.Add(request);
                continue;
            }

            if (age <= end && request.Release > TimeSpan.Zero)
            {
                var releaseProgress = (age - holdEnd).TotalMilliseconds / request.Release.TotalMilliseconds;
                var opacity = request.Opacity * (1 - Math.Clamp(releaseProgress, 0, 1));
                active.Add(request with { Opacity = opacity });
            }
        }

        _requests.RemoveAll(request => now - request.RequestedAt > request.Hold + request.Release);
        return active;
    }

    public void Clear() => _requests.Clear();
}
