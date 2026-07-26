using LumaBrake.Domain.Effects;

namespace LumaBrake.Effects.Composition;

public sealed class EffectComposer
{
    public FinalEffectState Compose(
        IEnumerable<EffectRequest> activeRequests,
        double userMaximumOpacity,
        TintEffect? tint = null)
    {
        var maximumOpacity = Math.Clamp(userMaximumOpacity, 0, 0.92);
        var finalTint = tint?.Clamp() ?? TintEffect.Disabled;
        var requests = activeRequests
            .Where(request => request.Opacity > 0)
            .OrderBy(request => Priority(request.Reason))
            .ToArray();

        if (requests.Length == 0)
        {
            return finalTint.Enabled ? FinalEffectState.Clear with { Tint = finalTint } : FinalEffectState.Clear;
        }

        var globalRequests = requests.Where(request => !request.IsRegional).ToArray();
        var opacity = 1.0;
        foreach (var request in globalRequests)
        {
            opacity *= 1 - Math.Clamp(request.Opacity, 0, 1);
        }

        var combined = globalRequests.Length == 0
            ? 0
            : Math.Clamp(1 - opacity, 0, maximumOpacity);
        var winningReason = requests[0].Reason;
        var reasons = requests.Select(request => request.Reason.ToString()).Distinct().ToArray();
        var regionalMasks = requests
            .Where(request => request.IsRegional)
            .SelectMany(request => request.Regions.Select(region => new RegionalEffectMask(
                request.Reason,
                RegionalOverlayOpacity(request.Opacity, combined, maximumOpacity),
                region.Clamp())))
            .Where(mask => mask.Opacity > 0 && mask.Region.Area > 0)
            .ToArray();

        return new FinalEffectState(combined, winningReason, reasons, regionalMasks, finalTint);
    }

    private static double RegionalOverlayOpacity(double requestedOpacity, double globalOpacity, double maximumOpacity)
    {
        var desiredFinalOpacity = Math.Clamp(requestedOpacity, 0, maximumOpacity);
        var currentGlobalOpacity = Math.Clamp(globalOpacity, 0, maximumOpacity);
        if (desiredFinalOpacity <= currentGlobalOpacity)
        {
            return 0;
        }

        return Math.Clamp(1 - ((1 - desiredFinalOpacity) / (1 - currentGlobalOpacity)), 0, maximumOpacity);
    }

    private static int Priority(EffectReason reason) => reason switch
    {
        EffectReason.Manual => 0,
        EffectReason.RepetitiveFlash => 1,
        EffectReason.BrightnessSpike => 2,
        EffectReason.StaticShade => 3,
        EffectReason.InactiveMonitor => 4,
        EffectReason.AdaptiveContent => 5,
        EffectReason.Scheduled => 6,
        _ => 99
    };
}
