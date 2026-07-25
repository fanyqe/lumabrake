using System.IO;
using Microsoft.Win32;

namespace LumaBrake.Platform.Windows;

public sealed record StartupRegistrationStatus(
    bool IsRegistered,
    bool IsCurrent,
    string? RegisteredCommand,
    string DesiredCommand);

public interface IStartupRegistrationStore
{
    string? ReadValue(string valueName);

    void WriteValue(string valueName, string command);

    void DeleteValue(string valueName);
}

public sealed class WindowsStartupRegistration
{
    public const string ValueName = "LumaBrake";
    public const string BackgroundArgument = "--background";

    private readonly IStartupRegistrationStore _store;

    public WindowsStartupRegistration()
        : this(new RegistryStartupRegistrationStore())
    {
    }

    public WindowsStartupRegistration(IStartupRegistrationStore store)
    {
        _store = store;
    }

    public StartupRegistrationStatus GetStatus(string executablePath)
    {
        var desiredCommand = BuildCommand(executablePath);
        var registeredCommand = _store.ReadValue(ValueName);
        return new StartupRegistrationStatus(
            !string.IsNullOrWhiteSpace(registeredCommand),
            string.Equals(registeredCommand, desiredCommand, StringComparison.OrdinalIgnoreCase),
            registeredCommand,
            desiredCommand);
    }

    public void Enable(string executablePath)
    {
        _store.WriteValue(ValueName, BuildCommand(executablePath));
    }

    public void Disable()
    {
        _store.DeleteValue(ValueName);
    }

    public static string BuildCommand(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Startup executable path is required.", nameof(executablePath));
        }

        if (executablePath.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("Startup executable path cannot contain quotes.", nameof(executablePath));
        }

        return $"\"{Path.GetFullPath(executablePath)}\" {BackgroundArgument}";
    }
}

internal sealed class RegistryStartupRegistrationStore : IStartupRegistrationStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? ReadValue(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(valueName) as string;
    }

    public void WriteValue(string valueName, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(valueName, command, RegistryValueKind.String);
    }

    public void DeleteValue(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
