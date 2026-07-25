using LumaBrake.Domain.Hardware;

namespace LumaBrake.Domain.Schedules;

public sealed record StartupRestorationOptions
{
    public bool Enabled { get; init; } = true;

    public bool ApplySchedules { get; init; } = true;

    public HardwareBrightnessTarget HardwareBrightness { get; init; } = new();
}
