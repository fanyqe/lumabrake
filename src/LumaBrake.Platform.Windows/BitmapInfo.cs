using System.Runtime.InteropServices;

namespace LumaBrake.Platform.Windows;

[StructLayout(LayoutKind.Sequential)]
public struct BitmapInfoHeader
{
    public uint Size;
    public int Width;
    public int Height;
    public ushort Planes;
    public ushort BitCount;
    public uint Compression;
    public uint SizeImage;
    public int XPelsPerMeter;
    public int YPelsPerMeter;
    public uint ClrUsed;
    public uint ClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
public struct BitmapInfo
{
    public BitmapInfoHeader Header;
    public uint Colors;
}
