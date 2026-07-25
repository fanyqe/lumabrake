using LumaBrake.Platform.Windows;

namespace LumaBrake.Platform.Windows.Tests;

public sealed class WindowsDisplayChangeNotifierTests
{
    [Fact]
    public void Constructor_SubscribesToDisplaySettingsChanges()
    {
        var source = new RecordingDisplaySettingsChangeSource();

        using var notifier = new WindowsDisplayChangeNotifier(() => { }, source);

        Assert.Equal(1, source.SubscribeCount);
        Assert.NotNull(source.Handler);
    }

    [Fact]
    public void DisplaySettingsChanged_RefreshesMonitors()
    {
        var source = new RecordingDisplaySettingsChangeSource();
        var refreshCount = 0;
        using var notifier = new WindowsDisplayChangeNotifier(() => refreshCount++, source);

        source.Raise();

        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public void Dispose_UnsubscribesAndIgnoresLaterDisplaySettingsChanges()
    {
        var source = new RecordingDisplaySettingsChangeSource();
        var refreshCount = 0;
        var notifier = new WindowsDisplayChangeNotifier(() => refreshCount++, source);

        notifier.Dispose();
        notifier.Dispose();
        source.Raise();
        source.RaiseUnsubscribed();

        Assert.Equal(1, source.UnsubscribeCount);
        Assert.Equal(0, refreshCount);
    }

    private sealed class RecordingDisplaySettingsChangeSource : IDisplaySettingsChangeSource
    {
        public int SubscribeCount { get; private set; }

        public int UnsubscribeCount { get; private set; }

        public EventHandler? Handler { get; private set; }

        private EventHandler? UnsubscribedHandler { get; set; }

        public void Subscribe(EventHandler handler)
        {
            SubscribeCount++;
            Handler = handler;
        }

        public void Unsubscribe(EventHandler handler)
        {
            UnsubscribeCount++;
            if (ReferenceEquals(Handler, handler))
            {
                UnsubscribedHandler = handler;
                Handler = null;
            }
        }

        public void Raise() => Handler?.Invoke(this, EventArgs.Empty);

        public void RaiseUnsubscribed() => UnsubscribedHandler?.Invoke(this, EventArgs.Empty);
    }
}
