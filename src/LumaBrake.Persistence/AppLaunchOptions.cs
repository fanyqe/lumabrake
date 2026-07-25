namespace LumaBrake.Persistence;

public sealed record AppLaunchOptions(bool SafeModeRequested, bool StartInBackground, bool SmokeStartupRequested)
{
    public static AppLaunchOptions FromArgs(IEnumerable<string> args)
    {
        var values = args.ToArray();
        return new AppLaunchOptions(
            SafeModeSettings.IsRequested(values),
            values.Any(IsBackgroundArgument),
            values.Any(IsSmokeStartupArgument));
    }

    private static bool IsBackgroundArgument(string arg) =>
        string.Equals(arg, "--background", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase);

    private static bool IsSmokeStartupArgument(string arg) =>
        string.Equals(arg, "--smoke-startup", StringComparison.OrdinalIgnoreCase);
}
