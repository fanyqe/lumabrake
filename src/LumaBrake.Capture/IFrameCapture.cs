using LumaBrake.Domain.Monitors;

namespace LumaBrake.Capture;

public interface IFrameCapture : IDisposable
{
    CapturedFrame Capture(MonitorDescriptor monitor);
}
