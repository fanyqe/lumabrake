using LumaBrake.Domain.Analysis;

namespace LumaBrake.Persistence;

public sealed record LumaBrakeSettings
{
    public bool Enabled { get; init; } = true;

    public double UserMaximumOpacity { get; init; } = 0.64;

    public double ManualDimmingOpacity { get; init; }

    public SpikeDetectorOptions SpikeGuard { get; init; } = new();

    public FlashDetectorOptions FlashGuard { get; init; } = new();
}
