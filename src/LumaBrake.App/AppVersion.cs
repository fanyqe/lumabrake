using System.Reflection;

namespace LumaBrake.App;

public static class AppVersion
{
    public static string DisplayVersion => $"v{ProductVersion()}";

    private static string ProductVersion()
    {
        var informationalVersion = Assembly
            .GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Split('+', StringSplitOptions.TrimEntries)[0];
        }

        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    }
}
