namespace LumaBrake.Platform.Windows;

public static class WindowInterop
{
    public static void MakeOverlayWindow(IntPtr hwnd)
    {
        var styles = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        styles |= NativeMethods.WsExLayered |
                  NativeMethods.WsExTransparent |
                  NativeMethods.WsExToolWindow |
                  NativeMethods.WsExNoActivate;

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(styles));
        _ = NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WdaExcludefromcapture);
    }
}
