namespace LumaBrake.Domain.Hardware;

public sealed record HardwareBrightnessTarget
{
    public bool Enabled { get; init; }

    public string DisplayDeviceName { get; init; } = string.Empty;

    public int PhysicalMonitorIndex { get; init; }

    public int Brightness { get; init; } = 100;

    public bool UseSoftwareFallback { get; init; }

    public HardwareBrightnessTarget Clamp() =>
        this with
        {
            Enabled = Enabled && !string.IsNullOrWhiteSpace(DisplayDeviceName),
            DisplayDeviceName = DisplayDeviceName?.Trim() ?? string.Empty,
            PhysicalMonitorIndex = Math.Max(PhysicalMonitorIndex, 0),
            Brightness = Math.Clamp(Brightness, 0, 100)
        };
}
