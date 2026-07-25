namespace LumaBrake.Platform.Windows;

public static class WindowInterop
{
    public static void MakeOverlayWindow(IntPtr hwnd)
    {
        var styles = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        styles = OverlayExtendedStyles(styles);

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(styles));
        _ = NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WdaExcludefromcapture);
    }

    public static long OverlayExtendedStyles(long existingStyles) =>
        existingStyles |
        NativeMethods.WsExLayered |
        NativeMethods.WsExTransparent |
        NativeMethods.WsExToolWindow |
        NativeMethods.WsExNoActivate;
}
