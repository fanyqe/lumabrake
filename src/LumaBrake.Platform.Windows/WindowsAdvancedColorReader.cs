using System.Runtime.InteropServices;
using LumaBrake.Domain.Monitors;

namespace LumaBrake.Platform.Windows;

public static class WindowsAdvancedColorReader
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    public static IReadOnlyDictionary<string, MonitorAdvancedColorState> ReadBySourceDeviceName()
    {
        try
        {
            return ReadBySourceDeviceNameCore();
        }
        catch (DllNotFoundException)
        {
            return new Dictionary<string, MonitorAdvancedColorState>(StringComparer.OrdinalIgnoreCase);
        }
        catch (EntryPointNotFoundException)
        {
            return new Dictionary<string, MonitorAdvancedColorState>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static IReadOnlyDictionary<string, MonitorAdvancedColorState> ReadBySourceDeviceNameCore()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var bufferResult = NativeMethods.GetDisplayConfigBufferSizes(
                NativeMethods.QdcOnlyActivePaths,
                out var pathCount,
                out var modeCount);

            if (bufferResult != ErrorSuccess || pathCount == 0)
            {
                return new Dictionary<string, MonitorAdvancedColorState>(StringComparer.OrdinalIgnoreCase);
            }

            var paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];
            var queryPathCount = pathCount;
            var queryModeCount = modeCount;
            var queryResult = NativeMethods.QueryDisplayConfig(
                NativeMethods.QdcOnlyActivePaths,
                ref queryPathCount,
                paths,
                ref queryModeCount,
                modes,
                IntPtr.Zero);

            if (queryResult == ErrorInsufficientBuffer)
            {
                continue;
            }

            if (queryResult != ErrorSuccess)
            {
                return new Dictionary<string, MonitorAdvancedColorState>(StringComparer.OrdinalIgnoreCase);
            }

            return BuildAdvancedColorMap(paths.Take((int)queryPathCount));
        }

        return new Dictionary<string, MonitorAdvancedColorState>(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, MonitorAdvancedColorState> BuildAdvancedColorMap(
        IEnumerable<DisplayConfigPathInfo> paths)
    {
        var states = new Dictionary<string, MonitorAdvancedColorState>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if ((path.Flags & NativeMethods.DisplayconfigPathActive) == 0 ||
                !TryReadSourceDeviceName(path.SourceInfo, out var sourceDeviceName))
            {
                continue;
            }

            var state = TryReadAdvancedColor(path.TargetInfo, out var advancedColor)
                ? advancedColor
                : MonitorAdvancedColorState.Unknown;

            states[sourceDeviceName] = states.TryGetValue(sourceDeviceName, out var existing)
                ? existing.Merge(state)
                : state;
        }

        return states;
    }

    private static bool TryReadSourceDeviceName(DisplayConfigPathSourceInfo sourceInfo, out string sourceDeviceName)
    {
        var request = new DisplayConfigSourceDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = NativeMethods.DisplayconfigDeviceInfoGetSourceName,
                Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                AdapterId = sourceInfo.AdapterId,
                Id = sourceInfo.Id
            },
            ViewGdiDeviceName = string.Empty
        };

        var result = NativeMethods.DisplayConfigGetDeviceInfo(ref request);
        sourceDeviceName = request.ViewGdiDeviceName.TrimEnd('\0');
        return result == ErrorSuccess && !string.IsNullOrWhiteSpace(sourceDeviceName);
    }

    private static bool TryReadAdvancedColor(DisplayConfigPathTargetInfo targetInfo, out MonitorAdvancedColorState state)
    {
        var request = new DisplayConfigGetAdvancedColorInfo
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = NativeMethods.DisplayconfigDeviceInfoGetAdvancedColorInfo,
                Size = (uint)Marshal.SizeOf<DisplayConfigGetAdvancedColorInfo>(),
                AdapterId = targetInfo.AdapterId,
                Id = targetInfo.Id
            }
        };

        var result = NativeMethods.DisplayConfigGetDeviceInfo(ref request);
        if (result != ErrorSuccess)
        {
            state = MonitorAdvancedColorState.Unknown;
            return false;
        }

        state = FromNativeValue(request.Value, request.ColorEncoding, request.BitsPerColorChannel);
        return true;
    }

    public static MonitorAdvancedColorState FromNativeValue(
        uint value,
        int colorEncoding,
        uint bitsPerColorChannel) =>
        new(
            IsKnown: true,
            IsSupported: (value & 0x1) != 0,
            IsEnabled: (value & 0x2) != 0,
            IsWideColorGamutEnabled: (value & 0x4) != 0,
            IsForceDisabled: (value & 0x8) != 0,
            ColorEncoding: FormatColorEncoding(colorEncoding),
            BitsPerColorChannel: bitsPerColorChannel);

    private static string FormatColorEncoding(int colorEncoding) => colorEncoding switch
    {
        0 => "RGB",
        1 => "YCbCr444",
        2 => "YCbCr422",
        3 => "YCbCr420",
        4 => "Intensity",
        _ => "Unknown"
    };
}
