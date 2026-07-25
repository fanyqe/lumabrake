namespace LumaBrake.Rendering;

public sealed class OverlayRenderSurfaceLostException : InvalidOperationException
{
    public OverlayRenderSurfaceLostException(string message)
        : base(message)
    {
    }

    public OverlayRenderSurfaceLostException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
