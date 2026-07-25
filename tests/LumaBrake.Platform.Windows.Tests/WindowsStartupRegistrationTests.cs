using LumaBrake.Platform.Windows;

namespace LumaBrake.Platform.Windows.Tests;

public sealed class WindowsStartupRegistrationTests
{
    [Fact]
    public void Enable_WritesQuotedBackgroundCommand()
    {
        var store = new MemoryStartupRegistrationStore();
        var registration = new WindowsStartupRegistration(store);

        registration.Enable(@"C:\Program Files\LumaBrake\LumaBrake.App.exe");

        Assert.Equal(
            "\"C:\\Program Files\\LumaBrake\\LumaBrake.App.exe\" --background",
            store.Values[WindowsStartupRegistration.ValueName]);
    }

    [Fact]
    public void GetStatus_DetectsCurrentRegistration()
    {
        var store = new MemoryStartupRegistrationStore();
        var registration = new WindowsStartupRegistration(store);
        registration.Enable(@"C:\Tools\LumaBrake.App.exe");

        var status = registration.GetStatus(@"C:\Tools\LumaBrake.App.exe");

        Assert.True(status.IsRegistered);
        Assert.True(status.IsCurrent);
    }

    [Fact]
    public void GetStatus_FlagsDifferentRegisteredCommand()
    {
        var store = new MemoryStartupRegistrationStore();
        store.WriteValue(WindowsStartupRegistration.ValueName, @"""C:\Other\LumaBrake.App.exe"" --background");
        var registration = new WindowsStartupRegistration(store);

        var status = registration.GetStatus(@"C:\Tools\LumaBrake.App.exe");

        Assert.True(status.IsRegistered);
        Assert.False(status.IsCurrent);
    }

    [Fact]
    public void Disable_RemovesRegistrationValue()
    {
        var store = new MemoryStartupRegistrationStore();
        var registration = new WindowsStartupRegistration(store);
        registration.Enable(@"C:\Tools\LumaBrake.App.exe");

        registration.Disable();

        Assert.False(store.Values.ContainsKey(WindowsStartupRegistration.ValueName));
    }

    [Fact]
    public void BuildCommand_RejectsQuotedExecutablePath()
    {
        Assert.Throws<ArgumentException>(() => WindowsStartupRegistration.BuildCommand("C:\\bad\"path\\LumaBrake.App.exe"));
    }

    private sealed class MemoryStartupRegistrationStore : IStartupRegistrationStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? ReadValue(string valueName) =>
            Values.GetValueOrDefault(valueName);

        public void WriteValue(string valueName, string command) =>
            Values[valueName] = command;

        public void DeleteValue(string valueName) =>
            Values.Remove(valueName);
    }
}
