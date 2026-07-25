namespace LumaBrake.Domain.Monitors;

public sealed record MonitorDescriptor(
    string StableId,
    string DeviceName,
    string DisplayName,
    bool IsPrimary,
    bool IsActive,
    MonitorBounds Bounds,
    MonitorBounds WorkArea,
    double DpiScale = 1.0,
    DateTimeOffset? LastSeenAt = null);
