using LumaBrake.Domain.Effects;

namespace LumaBrake.Effects.Composition;

public sealed class EffectEnvelope
{
    private readonly List<EffectRequest> _requests = [];

    public void Add(EffectRequest request)
    {
        if (request.Opacity <= 0)
        {
            _requests.RemoveAll(existing => existing.Reason == request.Reason && existing.IsRegional == request.IsRegional);
            return;
        }

        _requests.RemoveAll(existing => existing.Reason == request.Reason && existing.IsRegional == request.IsRegional);
        _requests.Add(request);
    }

    public void Remove(EffectReason reason) => _requests.RemoveAll(request => request.Reason == reason);

    public void ReleaseRegional(EffectReason reason, DateTimeOffset now, TimeSpan release)
    {
        if (release <= TimeSpan.Zero)
        {
            _requests.RemoveAll(request => request.Reason == reason && request.IsRegional);
            return;
        }

        var activeRegionalRequests = ActiveAt(now)
            .Where(request => request.Reason == reason && request.IsRegional && request.Opacity > 0)
            .ToArray();
        _requests.RemoveAll(request => request.Reason == reason && request.IsRegional);
        if (activeRegionalRequests.Length == 0)
        {
            return;
        }

        _requests.Add(new EffectRequest(
            reason,
            activeRegionalRequests.Max(request => request.Opacity),
            now,
            TimeSpan.Zero,
            release,
            activeRegionalRequests.SelectMany(request => request.Regions).ToArray()));
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
