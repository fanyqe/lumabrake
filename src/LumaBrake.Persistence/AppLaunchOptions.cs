namespace LumaBrake.Persistence;

public sealed record AppLaunchOptions(bool SafeModeRequested, bool StartInBackground)
{
    public static AppLaunchOptions FromArgs(IEnumerable<string> args)
    {
        var values = args.ToArray();
        return new AppLaunchOptions(
            SafeModeSettings.IsRequested(values),
            values.Any(IsBackgroundArgument));
    }

    private static bool IsBackgroundArgument(string arg) =>
        string.Equals(arg, "--background", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase);
}
