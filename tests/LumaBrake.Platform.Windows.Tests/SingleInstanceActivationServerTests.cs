using LumaBrake.Platform.Windows;

namespace LumaBrake.Platform.Windows.Tests;

public sealed class SingleInstanceActivationServerTests
{
    [Fact]
    public void NotifyExistingInstance_ReturnsFalseWhenServerDoesNotExist()
    {
        var eventName = CreateEventName();

        var notified = SingleInstanceActivationServer.NotifyExistingInstance(eventName);

        Assert.False(notified);
    }

    [Fact]
    public void NotifyExistingInstance_SignalsActivationServer()
    {
        var eventName = CreateEventName();
        using var received = new ManualResetEventSlim();
        using var server = SingleInstanceActivationServer.Start(() => received.Set(), eventName);

        var notified = SingleInstanceActivationServer.NotifyExistingInstance(eventName);

        Assert.True(notified);
        Assert.True(received.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Start_RejectsBlankEventName()
    {
        Assert.Throws<ArgumentException>(() => SingleInstanceActivationServer.Start(() => { }, " "));
    }

    private static string CreateEventName() =>
        $@"Local\LumaBrake.Tests.Activate.{Guid.NewGuid():N}";
}
