namespace LumaBrake.Persistence;

public static class SafeModeSettings
{
    public static LumaBrakeSettings Apply(LumaBrakeSettings settings) =>
        settings with
        {
            Enabled = false,
            UserMaximumOpacity = Math.Min(settings.UserMaximumOpacity, 0.25),
            ManualDimmingOpacity = 0,
            HardwareBrightness = settings.HardwareBrightness with
            {
                Enabled = false,
                AllowWrites = false
            }
        };

    public static bool IsRequested(IEnumerable<string> args) =>
        args.Any(arg => string.Equals(arg, "--safe-mode", StringComparison.OrdinalIgnoreCase));
}
