using LumaBrake.Platform.Windows;

namespace LumaBrake.Cli;

internal static class StartupCliCommands
{
    public static int Run(IReadOnlyList<string> args, WindowsStartupRegistration registration)
    {
        if (args.Count < 2)
        {
            return PrintUsage();
        }

        return args[1].ToLowerInvariant() switch
        {
            "status" => PrintStatus(registration),
            "enable" => Enable(registration),
            "disable" => Disable(registration),
            _ => PrintUsage()
        };
    }

    public static string ExpectedAppExecutablePath(string baseDirectory) =>
        Path.Combine(baseDirectory, "LumaBrake.App.exe");

    public static string FormatStatus(StartupRegistrationStatus status)
    {
        if (!status.IsRegistered)
        {
            return "disabled";
        }

        return status.IsCurrent ? "enabled" : "enabled with a different command";
    }

    private static int PrintStatus(WindowsStartupRegistration registration)
    {
        var appPath = ExpectedAppExecutablePath(AppContext.BaseDirectory);
        var status = registration.GetStatus(appPath);
        Console.WriteLine($"Windows startup: {FormatStatus(status)}");
        Console.WriteLine(File.Exists(appPath)
            ? $"App executable: {appPath}"
            : $"App executable: not found next to this CLI ({appPath})");

        if (!string.IsNullOrWhiteSpace(status.RegisteredCommand))
        {
            Console.WriteLine($"Registered command: {status.RegisteredCommand}");
        }

        Console.WriteLine($"Expected command: {status.DesiredCommand}");
        return 0;
    }

    private static int Enable(WindowsStartupRegistration registration)
    {
        var appPath = ExpectedAppExecutablePath(AppContext.BaseDirectory);
        if (!File.Exists(appPath))
        {
            Console.Error.WriteLine($"LumaBrake.App.exe was not found next to this CLI: {appPath}");
            Console.Error.WriteLine("Run this command from the published portable package folder.");
            return 2;
        }

        registration.Enable(appPath);
        Console.WriteLine("Windows startup enabled.");
        Console.WriteLine($"Startup command: {WindowsStartupRegistration.BuildCommand(appPath)}");
        return 0;
    }

    private static int Disable(WindowsStartupRegistration registration)
    {
        registration.Disable();
        Console.WriteLine("Windows startup disabled.");
        return 0;
    }

    private static int PrintUsage()
    {
        Console.Error.WriteLine("Usage: lumabrake startup <status|enable|disable>");
        return 2;
    }
}
