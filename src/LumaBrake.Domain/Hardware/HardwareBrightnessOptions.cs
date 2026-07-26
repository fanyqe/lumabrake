namespace LumaBrake.Domain.Hardware;

public sealed record HardwareBrightnessOptions
{
    public bool Enabled { get; init; }

    public bool AllowWrites { get; init; }

    public TimeSpan MinimumReadInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan MinimumWriteInterval { get; init; } = TimeSpan.FromSeconds(10);

    public string[] BlacklistedDisplayDeviceNames { get; init; } = [];

    public HardwareBrightnessOptions Clamp() =>
        this with
        {
            MinimumReadInterval = MinimumReadInterval < TimeSpan.Zero ? TimeSpan.Zero : MinimumReadInterval,
            MinimumWriteInterval = MinimumWriteInterval < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : MinimumWriteInterval,
            BlacklistedDisplayDeviceNames = (BlacklistedDisplayDeviceNames ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
}
