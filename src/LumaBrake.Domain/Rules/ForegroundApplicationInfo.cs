namespace LumaBrake.Domain.Rules;

public sealed record ForegroundApplicationInfo(string? ProcessName, string? WindowTitle, bool IsFullScreen = false)
{
    public static ForegroundApplicationInfo Unknown { get; } = new(null, null);
}
