namespace LumaBrake.MonitorControl;

public sealed record HardwareMonitorCapability(
    string DisplayDeviceName,
    string PhysicalDescription,
    int PhysicalMonitorIndex,
    bool SupportsDdcCi,
    int CapabilitiesLength,
    string? Error);
