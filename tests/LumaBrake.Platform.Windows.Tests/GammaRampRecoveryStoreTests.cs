using LumaBrake.Domain.Color;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Monitors;
using LumaBrake.Platform.Windows;

namespace LumaBrake.Platform.Windows.Tests;

public sealed class GammaRampRecoveryStoreTests
{
    [Fact]
    public void Store_RoundTripsAndRemovesRecoveryEntries()
    {
        var root = TempRoot();
        var store = new GammaRampRecoveryStore(root);
        var original = GammaRamp.FromMultipliers(1, 0.9, 0.8, offset: 0);

        Assert.True(store.TryUpsert(@"\\.\DISPLAY1", original.Values));

        var entry = Assert.Single(store.Load());
        Assert.Equal(@"\\.\DISPLAY1", entry.DeviceName);
        Assert.Equal(original.Values, entry.Values);

        store.Remove(@"\\.\DISPLAY1");

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Controller_SavesOriginalRampBeforeApplyAndRemovesItAfterClear()
    {
        var root = TempRoot();
        var store = new GammaRampRecoveryStore(root);
        var accessor = new RecordingGammaRampAccessor();
        var original = GammaRamp.FromMultipliers(1, 1, 1, offset: 0);
        accessor.SetCurrent(@"\\.\DISPLAY1", original);
        using var controller = new WindowsGammaRampNightColorController(accessor, store);

        var applied = controller.Apply(
            [Monitor(@"\\.\DISPLAY1")],
            new NightColorState(true, 3400, 0.80, 1, TintEffect.Disabled, 0.20, "manual"));

        Assert.True(applied);
        Assert.Single(store.Load());
        Assert.NotEqual(original, accessor.Current(@"\\.\DISPLAY1"));

        controller.Clear();

        Assert.Equal(original, accessor.Current(@"\\.\DISPLAY1"));
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Controller_AppliesStableRampWithoutArtificialChannelOffset()
    {
        var root = TempRoot();
        var store = new GammaRampRecoveryStore(root);
        var accessor = new RecordingGammaRampAccessor();
        accessor.SetCurrent(@"\\.\DISPLAY1", GammaRamp.Identity);
        using var controller = new WindowsGammaRampNightColorController(accessor, store);
        var state = new NightColorState(true, 3400, 0.80, 1, TintEffect.Disabled, 0.20, "manual");

        var applied = controller.Apply([Monitor(@"\\.\DISPLAY1")], state);

        var expected = GammaRamp.FromMultipliers(
            NightColorGammaRamp.RedMultiplier(state.TemperatureKelvin) * state.Brightness,
            NightColorGammaRamp.GreenMultiplier(state.TemperatureKelvin) * state.Brightness,
            NightColorGammaRamp.BlueMultiplier(state.TemperatureKelvin) * state.Brightness,
            offset: 0);
        Assert.True(applied);
        Assert.Equal(expected, accessor.Current(@"\\.\DISPLAY1"));
    }

    [Fact]
    public void Controller_RestoresPersistedOriginalRampOnNextStartupAfterCrash()
    {
        var root = TempRoot();
        var store = new GammaRampRecoveryStore(root);
        var accessor = new RecordingGammaRampAccessor();
        var original = GammaRamp.FromMultipliers(1, 1, 1, offset: 0);
        accessor.SetCurrent(@"\\.\DISPLAY1", original);
        var crashedController = new WindowsGammaRampNightColorController(accessor, store);

        var applied = crashedController.Apply(
            [Monitor(@"\\.\DISPLAY1")],
            new NightColorState(true, 3400, 0.80, 1, TintEffect.Disabled, 0.20, "manual"));

        Assert.True(applied);
        Assert.Single(store.Load());
        Assert.NotEqual(original, accessor.Current(@"\\.\DISPLAY1"));

        _ = new WindowsGammaRampNightColorController(accessor, new GammaRampRecoveryStore(root));

        Assert.Equal(original, accessor.Current(@"\\.\DISPLAY1"));
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Controller_DisabledStateNeutralizesUnknownStuckRampOnce()
    {
        var root = TempRoot();
        var store = new GammaRampRecoveryStore(root);
        var accessor = new RecordingGammaRampAccessor();
        accessor.SetCurrent(@"\\.\DISPLAY1", GammaRamp.FromMultipliers(1, 0.8, 0.45, offset: 0));
        using var controller = new WindowsGammaRampNightColorController(accessor, store);

        var applied = controller.Apply(
            [Monitor(@"\\.\DISPLAY1")],
            NightColorState.Disabled);

        Assert.False(applied);
        Assert.Equal(GammaRamp.Identity, accessor.Current(@"\\.\DISPLAY1"));
        Assert.Equal(1, accessor.SetCount(@"\\.\DISPLAY1"));

        controller.Apply(
            [Monitor(@"\\.\DISPLAY1")],
            NightColorState.Disabled);

        Assert.Equal(1, accessor.SetCount(@"\\.\DISPLAY1"));
    }

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lumabrake-gamma-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static MonitorDescriptor Monitor(string deviceName) => new(
        deviceName,
        deviceName,
        "Test monitor",
        IsPrimary: true,
        IsActive: true,
        new MonitorBounds(0, 0, 1920, 1080),
        new MonitorBounds(0, 0, 1920, 1040));

    private sealed class RecordingGammaRampAccessor : IDisplayGammaRampAccessor
    {
        private readonly Dictionary<string, GammaRamp> _current = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _setCounts = new(StringComparer.OrdinalIgnoreCase);

        public bool TryGet(string deviceName, out GammaRamp ramp) =>
            _current.TryGetValue(deviceName, out ramp!);

        public bool TrySet(string deviceName, GammaRamp ramp)
        {
            if (!_current.ContainsKey(deviceName))
            {
                return false;
            }

            _current[deviceName] = ramp;
            _setCounts[deviceName] = SetCount(deviceName) + 1;
            return true;
        }

        public void SetCurrent(string deviceName, GammaRamp ramp) => _current[deviceName] = ramp;

        public GammaRamp Current(string deviceName) => _current[deviceName];

        public int SetCount(string deviceName) => _setCounts.GetValueOrDefault(deviceName);
    }
}
