namespace LumaBrake.MonitorControl;

public sealed record HardwareBrightnessReading(
    string DisplayDeviceName,
    string PhysicalDescription,
    bool IsAvailable,
    int Minimum,
    int Current,
    int Maximum,
    string? Error);
