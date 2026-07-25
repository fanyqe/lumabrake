using LumaBrake.Domain.Effects;

namespace LumaBrake.Effects.Composition;

public sealed class EffectComposer
{
    private const double RegionalMaskPadding = 0.00625;

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

        return new FinalEffectState(combined, winningReason, reasons, MergeRegionalMasks(regionalMasks), finalTint);
    }

    private static IReadOnlyList<RegionalEffectMask> MergeRegionalMasks(IReadOnlyList<RegionalEffectMask> masks)
    {
        var merged = new List<RegionalEffectMask>();
        foreach (var mask in masks)
        {
            var candidate = mask with { Region = mask.Region.Pad(RegionalMaskPadding) };
            var mergedIntoExisting = false;
            for (var index = 0; index < merged.Count; index++)
            {
                var current = merged[index];
                if (!CanMerge(current, candidate))
                {
                    continue;
                }

                merged[index] = current with { Region = current.Region.Union(candidate.Region) };
                mergedIntoExisting = true;
                break;
            }

            if (!mergedIntoExisting)
            {
                merged.Add(candidate);
            }
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            for (var index = 0; index < merged.Count; index++)
            {
                for (var otherIndex = index + 1; otherIndex < merged.Count; otherIndex++)
                {
                    if (!CanMerge(merged[index], merged[otherIndex]))
                    {
                        continue;
                    }

                    merged[index] = merged[index] with { Region = merged[index].Region.Union(merged[otherIndex].Region) };
                    merged.RemoveAt(otherIndex);
                    changed = true;
                    break;
                }

                if (changed)
                {
                    break;
                }
            }
        }

        return merged;
    }

    private static bool CanMerge(RegionalEffectMask first, RegionalEffectMask second) =>
        first.Reason == second.Reason &&
        Math.Abs(first.Opacity - second.Opacity) < 0.001 &&
        first.Region.TouchesOrOverlaps(second.Region);

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
        EffectReason.FocusDimming => 6,
        EffectReason.Scheduled => 7,
        EffectReason.NightColor => 8,
        _ => 99
    };
}
