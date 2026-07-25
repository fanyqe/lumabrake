using System.Windows;

namespace LumaBrake.Platform.Windows;

public static class PrimaryMonitor
{
    private const int SmCxscreen = 0;
    private const int SmCyscreen = 1;

    public static Rect Bounds()
    {
        var width = NativeMethods.GetSystemMetrics(SmCxscreen);
        var height = NativeMethods.GetSystemMetrics(SmCyscreen);

        return new Rect(0, 0, width, height);
    }
}
