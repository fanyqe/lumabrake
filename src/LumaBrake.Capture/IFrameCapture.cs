namespace LumaBrake.Capture;

public interface IFrameCapture : IDisposable
{
    CapturedFrame Capture();
}
