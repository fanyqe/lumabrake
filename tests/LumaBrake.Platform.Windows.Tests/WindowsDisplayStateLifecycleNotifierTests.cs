using Microsoft.Win32;
using LumaBrake.Platform.Windows;

namespace LumaBrake.Platform.Windows.Tests;

public sealed class WindowsDisplayStateLifecycleNotifierTests
{
    [Fact]
    public void Constructor_SubscribesToLifecycleEvents()
    {
        var power = new RecordingPowerModeChangeSource();
        var sessionSwitch = new RecordingSessionSwitchSource();
        var sessionEnding = new RecordingSessionEndingSource();

        using var notifier = new WindowsDisplayStateLifecycleNotifier(
            () => { },
            () => { },
            power,
            sessionSwitch,
            sessionEnding);

        Assert.Equal(1, power.SubscribeCount);
        Assert.Equal(1, sessionSwitch.SubscribeCount);
        Assert.Equal(1, sessionEnding.SubscribeCount);
    }

    [Fact]
    public void PowerSuspend_ClearsDisplayState()
    {
        var power = new RecordingPowerModeChangeSource();
        var clearCount = 0;
        var refreshCount = 0;
        using var notifier = new WindowsDisplayStateLifecycleNotifier(
            () => clearCount++,
            () => refreshCount++,
            power,
            new RecordingSessionSwitchSource(),
            new RecordingSessionEndingSource());

        power.Raise(PowerModes.Suspend);

        Assert.Equal(1, clearCount);
        Assert.Equal(0, refreshCount);
    }

    [Fact]
    public void PowerResume_RefreshesDisplayState()
    {
        var power = new RecordingPowerModeChangeSource();
        var clearCount = 0;
        var refreshCount = 0;
        using var notifier = new WindowsDisplayStateLifecycleNotifier(
            () => clearCount++,
            () => refreshCount++,
            power,
            new RecordingSessionSwitchSource(),
            new RecordingSessionEndingSource());

        power.Raise(PowerModes.Resume);

        Assert.Equal(0, clearCount);
        Assert.Equal(1, refreshCount);
    }

    [Theory]
    [InlineData(SessionSwitchReason.SessionLock)]
    [InlineData(SessionSwitchReason.ConsoleDisconnect)]
    [InlineData(SessionSwitchReason.RemoteDisconnect)]
    public void SessionDisconnect_ClearsDisplayState(SessionSwitchReason reason)
    {
        var sessionSwitch = new RecordingSessionSwitchSource();
        var clearCount = 0;
        var refreshCount = 0;
        using var notifier = new WindowsDisplayStateLifecycleNotifier(
            () => clearCount++,
            () => refreshCount++,
            new RecordingPowerModeChangeSource(),
            sessionSwitch,
            new RecordingSessionEndingSource());

        sessionSwitch.Raise(reason);

        Assert.Equal(1, clearCount);
        Assert.Equal(0, refreshCount);
    }

    [Theory]
    [InlineData(SessionSwitchReason.SessionUnlock)]
    [InlineData(SessionSwitchReason.ConsoleConnect)]
    [InlineData(SessionSwitchReason.RemoteConnect)]
    public void SessionReconnect_RefreshesDisplayState(SessionSwitchReason reason)
    {
        var sessionSwitch = new RecordingSessionSwitchSource();
        var clearCount = 0;
        var refreshCount = 0;
        using var notifier = new WindowsDisplayStateLifecycleNotifier(
            () => clearCount++,
            () => refreshCount++,
            new RecordingPowerModeChangeSource(),
            sessionSwitch,
            new RecordingSessionEndingSource());

        sessionSwitch.Raise(reason);

        Assert.Equal(0, clearCount);
        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public void SessionEnding_ClearsDisplayState()
    {
        var sessionEnding = new RecordingSessionEndingSource();
        var clearCount = 0;
        using var notifier = new WindowsDisplayStateLifecycleNotifier(
            () => clearCount++,
            () => { },
            new RecordingPowerModeChangeSource(),
            new RecordingSessionSwitchSource(),
            sessionEnding);

        sessionEnding.Raise();

        Assert.Equal(1, clearCount);
    }

    [Fact]
    public void Dispose_UnsubscribesAndIgnoresLaterEvents()
    {
        var power = new RecordingPowerModeChangeSource();
        var sessionSwitch = new RecordingSessionSwitchSource();
        var sessionEnding = new RecordingSessionEndingSource();
        var clearCount = 0;
        var refreshCount = 0;
        var notifier = new WindowsDisplayStateLifecycleNotifier(
            () => clearCount++,
            () => refreshCount++,
            power,
            sessionSwitch,
            sessionEnding);

        notifier.Dispose();
        notifier.Dispose();
        power.RaiseUnsubscribed(PowerModes.Suspend);
        sessionSwitch.RaiseUnsubscribed(SessionSwitchReason.SessionUnlock);
        sessionEnding.RaiseUnsubscribed();

        Assert.Equal(1, power.UnsubscribeCount);
        Assert.Equal(1, sessionSwitch.UnsubscribeCount);
        Assert.Equal(1, sessionEnding.UnsubscribeCount);
        Assert.Equal(0, clearCount);
        Assert.Equal(0, refreshCount);
    }

    private sealed class RecordingPowerModeChangeSource : IPowerModeChangeSource
    {
        public int SubscribeCount { get; private set; }

        public int UnsubscribeCount { get; private set; }

        private PowerModeChangedEventHandler? Handler { get; set; }

        private PowerModeChangedEventHandler? UnsubscribedHandler { get; set; }

        public void Subscribe(PowerModeChangedEventHandler handler)
        {
            SubscribeCount++;
            Handler = handler;
        }

        public void Unsubscribe(PowerModeChangedEventHandler handler)
        {
            UnsubscribeCount++;
            if (ReferenceEquals(Handler, handler))
            {
                UnsubscribedHandler = handler;
                Handler = null;
            }
        }

        public void Raise(PowerModes mode) => Handler?.Invoke(this, new PowerModeChangedEventArgs(mode));

        public void RaiseUnsubscribed(PowerModes mode) =>
            UnsubscribedHandler?.Invoke(this, new PowerModeChangedEventArgs(mode));
    }

    private sealed class RecordingSessionSwitchSource : ISessionSwitchSource
    {
        public int SubscribeCount { get; private set; }

        public int UnsubscribeCount { get; private set; }

        private SessionSwitchEventHandler? Handler { get; set; }

        private SessionSwitchEventHandler? UnsubscribedHandler { get; set; }

        public void Subscribe(SessionSwitchEventHandler handler)
        {
            SubscribeCount++;
            Handler = handler;
        }

        public void Unsubscribe(SessionSwitchEventHandler handler)
        {
            UnsubscribeCount++;
            if (ReferenceEquals(Handler, handler))
            {
                UnsubscribedHandler = handler;
                Handler = null;
            }
        }

        public void Raise(SessionSwitchReason reason) => Handler?.Invoke(this, new SessionSwitchEventArgs(reason));

        public void RaiseUnsubscribed(SessionSwitchReason reason) =>
            UnsubscribedHandler?.Invoke(this, new SessionSwitchEventArgs(reason));
    }

    private sealed class RecordingSessionEndingSource : ISessionEndingSource
    {
        public int SubscribeCount { get; private set; }

        public int UnsubscribeCount { get; private set; }

        private SessionEndingEventHandler? Handler { get; set; }

        private SessionEndingEventHandler? UnsubscribedHandler { get; set; }

        public void Subscribe(SessionEndingEventHandler handler)
        {
            SubscribeCount++;
            Handler = handler;
        }

        public void Unsubscribe(SessionEndingEventHandler handler)
        {
            UnsubscribeCount++;
            if (ReferenceEquals(Handler, handler))
            {
                UnsubscribedHandler = handler;
                Handler = null;
            }
        }

        public void Raise() => Handler?.Invoke(this, new SessionEndingEventArgs(SessionEndReasons.SystemShutdown));

        public void RaiseUnsubscribed() =>
            UnsubscribedHandler?.Invoke(this, new SessionEndingEventArgs(SessionEndReasons.SystemShutdown));
    }
}
