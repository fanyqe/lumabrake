using LumaBrake.Platform.Windows;

namespace LumaBrake.Platform.Windows.Tests;

public sealed class WindowsAdvancedColorReaderTests
{
    [Fact]
    public void FromNativeValue_DecodesAdvancedColorFlags()
    {
        var state = WindowsAdvancedColorReader.FromNativeValue(
            value: 0x1 | 0x2 | 0x4,
            colorEncoding: 0,
            bitsPerColorChannel: 10);

        Assert.True(state.IsKnown);
        Assert.True(state.IsSupported);
        Assert.True(state.IsEnabled);
        Assert.True(state.IsWideColorGamutEnabled);
        Assert.False(state.IsForceDisabled);
        Assert.Equal("RGB", state.ColorEncoding);
        Assert.Equal((uint)10, state.BitsPerColorChannel);
    }

    [Fact]
    public void FromNativeValue_DecodesForcedDisabledState()
    {
        var state = WindowsAdvancedColorReader.FromNativeValue(
            value: 0x1 | 0x8,
            colorEncoding: 3,
            bitsPerColorChannel: 8);

        Assert.True(state.IsKnown);
        Assert.True(state.IsSupported);
        Assert.False(state.IsEnabled);
        Assert.True(state.IsForceDisabled);
        Assert.Equal("YCbCr420", state.ColorEncoding);
        Assert.Equal((uint)8, state.BitsPerColorChannel);
    }
}
