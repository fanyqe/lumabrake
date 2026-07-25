namespace LumaBrake.Capture;

public sealed class FrameCaptureDeviceLostException : InvalidOperationException
{
    public FrameCaptureDeviceLostException(string message)
        : base(message)
    {
    }

    public FrameCaptureDeviceLostException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
