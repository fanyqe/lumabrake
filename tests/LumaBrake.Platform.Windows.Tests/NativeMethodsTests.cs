using LumaBrake.Platform.Windows;

namespace LumaBrake.Platform.Windows.Tests;

public sealed class NativeMethodsTests
{
    [Fact]
    public void WindowLongPtrImports_AreResolvable()
    {
        var exception = Record.Exception(() =>
        {
            _ = NativeMethods.GetWindowLongPtr(IntPtr.Zero, NativeMethods.GwlExStyle);
            _ = NativeMethods.SetWindowLongPtr(IntPtr.Zero, NativeMethods.GwlExStyle, IntPtr.Zero);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void OverlayExtendedStyles_KeepOverlayClickThroughAndNonActivating()
    {
        const long existingAppStyle = 0x00040000;

        var styles = WindowInterop.OverlayExtendedStyles(existingAppStyle);

        Assert.Equal(existingAppStyle, styles & existingAppStyle);
        Assert.NotEqual(0, styles & NativeMethods.WsExLayered);
        Assert.NotEqual(0, styles & NativeMethods.WsExTransparent);
        Assert.NotEqual(0, styles & NativeMethods.WsExToolWindow);
        Assert.NotEqual(0, styles & NativeMethods.WsExNoActivate);
        Assert.Equal(0x00000011, NativeMethods.WdaExcludefromcapture);
    }
}
