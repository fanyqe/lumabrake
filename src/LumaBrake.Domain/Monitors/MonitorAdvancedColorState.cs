namespace LumaBrake.Domain.Monitors;

public sealed record MonitorAdvancedColorState(
    bool IsKnown,
    bool IsSupported,
    bool IsEnabled,
    bool IsWideColorGamutEnabled,
    bool IsForceDisabled,
    string ColorEncoding,
    uint BitsPerColorChannel)
{
    public static MonitorAdvancedColorState Unknown { get; } = new(
        IsKnown: false,
        IsSupported: false,
        IsEnabled: false,
        IsWideColorGamutEnabled: false,
        IsForceDisabled: false,
        ColorEncoding: "Unknown",
        BitsPerColorChannel: 0);

    public bool RequiresConservativeHandling => IsKnown && IsEnabled;

    public MonitorAdvancedColorState Merge(MonitorAdvancedColorState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!IsKnown)
        {
            return other;
        }

        if (!other.IsKnown)
        {
            return this;
        }

        return this with
        {
            IsSupported = IsSupported || other.IsSupported,
            IsEnabled = IsEnabled || other.IsEnabled,
            IsWideColorGamutEnabled = IsWideColorGamutEnabled || other.IsWideColorGamutEnabled,
            IsForceDisabled = IsForceDisabled && other.IsForceDisabled,
            ColorEncoding = string.Equals(ColorEncoding, other.ColorEncoding, StringComparison.OrdinalIgnoreCase)
                ? ColorEncoding
                : "Mixed",
            BitsPerColorChannel = Math.Max(BitsPerColorChannel, other.BitsPerColorChannel)
        };
    }
}
