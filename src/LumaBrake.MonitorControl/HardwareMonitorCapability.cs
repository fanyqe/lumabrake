namespace LumaBrake.MonitorControl;

public sealed record HardwareMonitorCapability(
    string DisplayDeviceName,
    string PhysicalDescription,
    bool SupportsDdcCi,
    int CapabilitiesLength,
    string? Error);
