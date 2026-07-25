using LumaBrake.Domain.Hardware;

namespace LumaBrake.MonitorControl;

public interface IHardwareBrightnessWriter
{
    IReadOnlyList<HardwareBrightnessWriteResult> SetBrightness(
        string displayDeviceName,
        int physicalMonitorIndex,
        int requestedBrightness,
        HardwareBrightnessOptions options,
        DateTimeOffset now,
        DateTimeOffset? lastWriteAt);
}
