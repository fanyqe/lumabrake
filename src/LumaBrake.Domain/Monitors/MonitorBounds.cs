namespace LumaBrake.Domain.Monitors;

public sealed record MonitorBounds(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;
}
