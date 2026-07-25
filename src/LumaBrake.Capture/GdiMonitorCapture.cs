using System.ComponentModel;
using System.Runtime.InteropServices;
using LumaBrake.Domain.Frames;
using LumaBrake.Domain.Monitors;
using LumaBrake.Platform.Windows;

namespace LumaBrake.Capture;

public sealed class GdiMonitorCapture : IFrameCapture
{
    private readonly int _analysisWidth;
    private readonly int _analysisHeight;
    private bool _disposed;

    public GdiMonitorCapture(int analysisWidth = 160, int analysisHeight = 90)
    {
        _analysisWidth = analysisWidth;
        _analysisHeight = analysisHeight;
    }

    public CapturedFrame Capture(MonitorDescriptor monitor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (monitor.Bounds.Width <= 0 || monitor.Bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monitor), "Monitor bounds must be positive.");
        }

        var bounds = monitor.Bounds;
        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw CaptureDeviceLost("Screen device context could not be acquired.");
        }

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var oldObject = IntPtr.Zero;

        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
            {
                throw CaptureDeviceLost("Memory device context could not be created.");
            }

            var bitmapInfo = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = _analysisWidth,
                    Height = -_analysisHeight,
                    Planes = 1,
                    BitCount = 32,
                    Compression = NativeMethods.BiRgb,
                    SizeImage = (uint)(_analysisWidth * _analysisHeight * 4)
                }
            };

            bitmap = NativeMethods.CreateDIBSection(
                memoryDc,
                ref bitmapInfo,
                NativeMethods.DibRgbColors,
                out var bits,
                IntPtr.Zero,
                0);

            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                throw CaptureDeviceLost("Capture bitmap could not be created.");
            }

            oldObject = NativeMethods.SelectObject(memoryDc, bitmap);
            var copied = NativeMethods.StretchBlt(
                memoryDc,
                0,
                0,
                _analysisWidth,
                _analysisHeight,
                screenDc,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                NativeMethods.SrcCopy);

            if (!copied)
            {
                throw CaptureDeviceLost("Screen frame could not be copied.");
            }

            var buffer = new byte[_analysisWidth * _analysisHeight * 4];
            Marshal.Copy(bits, buffer, 0, buffer.Length);

            return new CapturedFrame(
                buffer,
                new FrameMetadata(DateTimeOffset.UtcNow, _analysisWidth, _analysisHeight, monitor.DeviceName),
                monitor);
        }
        finally
        {
            if (oldObject != IntPtr.Zero)
            {
                NativeMethods.SelectObject(memoryDc, oldObject);
            }

            if (bitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(memoryDc);
            }

            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public void Dispose() => _disposed = true;

    private static FrameCaptureDeviceLostException CaptureDeviceLost(string message)
    {
        var errorCode = Marshal.GetLastPInvokeError();
        if (errorCode == 0)
        {
            return new FrameCaptureDeviceLostException(message);
        }

        return new FrameCaptureDeviceLostException(
            $"{message} Win32 error {errorCode}.",
            new Win32Exception(errorCode));
    }
}
