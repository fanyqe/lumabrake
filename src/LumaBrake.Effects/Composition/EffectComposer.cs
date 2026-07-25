using LumaBrake.Domain.Effects;

namespace LumaBrake.Effects.Composition;

public sealed class EffectComposer
{
    public FinalEffectState Compose(IEnumerable<EffectRequest> activeRequests, double userMaximumOpacity)
    {
        var requests = activeRequests
            .Where(request => request.Opacity > 0)
            .OrderBy(request => Priority(request.Reason))
            .ToArray();

        if (requests.Length == 0)
        {
            return FinalEffectState.Clear;
        }

        var opacity = 1.0;
        foreach (var request in requests)
        {
            opacity *= 1 - Math.Clamp(request.Opacity, 0, 1);
        }

        var combined = Math.Clamp(1 - opacity, 0, Math.Clamp(userMaximumOpacity, 0, 0.92));
        var winningReason = requests[0].Reason;
        var reasons = requests.Select(request => request.Reason.ToString()).Distinct().ToArray();

        return new FinalEffectState(combined, winningReason, reasons);
    }

    private static int Priority(EffectReason reason) => reason switch
    {
        EffectReason.Manual => 0,
        EffectReason.RepetitiveFlash => 1,
        EffectReason.BrightnessSpike => 2,
        EffectReason.StaticShade => 3,
        EffectReason.AdaptiveContent => 4,
        _ => 99
    };
}
