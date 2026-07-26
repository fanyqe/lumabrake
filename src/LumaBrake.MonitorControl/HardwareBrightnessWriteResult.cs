namespace LumaBrake.MonitorControl;

public sealed record HardwareBrightnessWriteResult(
    string DisplayDeviceName,
    string PhysicalDescription,
    int PhysicalMonitorIndex,
    int RequestedBrightness,
    int AppliedHardwareBrightness,
    bool Succeeded,
    string? Error);
