using System.Runtime.InteropServices;
using LumaBrake.Domain.Color;
using LumaBrake.Domain.Monitors;

namespace LumaBrake.Platform.Windows;

public sealed class WindowsGammaRampNightColorController : INightColorDisplayController
{
    private static readonly TimeSpan GammaPollingInterval = TimeSpan.FromSeconds(10);

    private readonly IDisplayGammaRampAccessor _accessor;
    private readonly GammaRampRecoveryStore? _recoveryStore;
    private readonly Dictionary<string, GammaRamp> _originalRamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GammaRamp> _lastAppliedRamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _neutralizedUnknownDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private int? _lastTemperatureKelvin;
    private double? _lastBrightness;
    private DateTimeOffset _lastUpdateAt = DateTimeOffset.MinValue;
    private string[] _lastTargetDevices = [];
    private bool _disposed;

    public WindowsGammaRampNightColorController()
        : this(accessor: new WindowsDisplayGammaRampAccessor(), recoveryStore: null)
    {
    }

    public WindowsGammaRampNightColorController(string recoveryRootDirectory)
        : this(new WindowsDisplayGammaRampAccessor(), new GammaRampRecoveryStore(recoveryRootDirectory))
    {
    }

    internal WindowsGammaRampNightColorController(IDisplayGammaRampAccessor accessor, GammaRampRecoveryStore? recoveryStore)
    {
        _accessor = accessor;
        _recoveryStore = recoveryStore;
        RestoreRecoveredRamps();
    }

    public bool Apply(IReadOnlyList<MonitorDescriptor> monitors, NightColorState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            var targets = GammaRampTargets(monitors);
            if (!state.Enabled)
            {
                ClearCore(targets.Select(monitor => monitor.DeviceName));
                return false;
            }

            if (targets.Length == 0)
            {
                ClearCore();
                return false;
            }

            if (targets.Length != monitors.Count(monitor => monitor.IsActive))
            {
                ClearCore();
                return false;
            }

            var targetDevices = targets.Select(monitor => monitor.DeviceName).ToArray();
            if (!ShouldRefreshGamma(targetDevices, state))
            {
                return true;
            }

            var ramp = GammaRamp.FromMultipliers(
                NightColorGammaRamp.RedMultiplier(state.TemperatureKelvin) * state.Brightness,
                NightColorGammaRamp.GreenMultiplier(state.TemperatureKelvin) * state.Brightness,
                NightColorGammaRamp.BlueMultiplier(state.TemperatureKelvin) * state.Brightness,
                offset: 0);

            var appliedDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var monitor in targets)
            {
                if (!TryApply(monitor.DeviceName, ramp))
                {
                    RestoreDevices(appliedDevices);
                    ClearCore();
                    return false;
                }

                appliedDevices.Add(monitor.DeviceName);
            }

            foreach (var staleDevice in _lastAppliedRamps.Keys
                .Where(device => !appliedDevices.Contains(device))
                .ToArray())
            {
                RestoreDevice(staleDevice);
            }

            _lastTemperatureKelvin = state.TemperatureKelvin;
            _lastBrightness = state.Brightness;
            _lastTargetDevices = targetDevices;
            _lastUpdateAt = DateTimeOffset.UtcNow;
            return true;
        }
    }

    public void Clear()
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            ClearCore();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Clear();
        _disposed = true;
    }

    private bool TryApply(string deviceName, GammaRamp ramp)
    {
        if (_lastAppliedRamps.TryGetValue(deviceName, out var previous) && previous.Equals(ramp))
        {
            return true;
        }

        if (!_originalRamps.ContainsKey(deviceName))
        {
            if (!_accessor.TryGet(deviceName, out var original))
            {
                return false;
            }

            if (_recoveryStore is not null && !_recoveryStore.TryUpsert(deviceName, original.Values))
            {
                return false;
            }

            _originalRamps[deviceName] = original;
        }

        if (!_accessor.TrySet(deviceName, ramp))
        {
            return false;
        }

        _lastAppliedRamps[deviceName] = ramp;
        _neutralizedUnknownDevices.Remove(deviceName);
        return true;
    }

    private void ClearCore(IEnumerable<string>? neutralTargetDevices = null)
    {
        var knownOriginalDevices = _originalRamps.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var deviceName in knownOriginalDevices.ToArray())
        {
            RestoreDevice(deviceName);
        }

        RestoreUnknownTargetsToNeutral(neutralTargetDevices, knownOriginalDevices);
        _lastTemperatureKelvin = null;
        _lastBrightness = null;
        _lastTargetDevices = [];
        _lastUpdateAt = DateTimeOffset.MinValue;
    }

    private void RestoreUnknownTargetsToNeutral(
        IEnumerable<string>? deviceNames,
        HashSet<string> knownOriginalDevices)
    {
        if (deviceNames is null)
        {
            return;
        }

        foreach (var deviceName in deviceNames
            .Where(device => !string.IsNullOrWhiteSpace(device))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (knownOriginalDevices.Contains(deviceName) || !_neutralizedUnknownDevices.Add(deviceName))
            {
                continue;
            }

            if (_accessor.TrySet(deviceName, GammaRamp.Identity))
            {
                _lastAppliedRamps.Remove(deviceName);
                _recoveryStore?.Remove(deviceName);
            }
            else
            {
                _neutralizedUnknownDevices.Remove(deviceName);
            }
        }
    }

    private void RestoreDevices(IEnumerable<string> deviceNames)
    {
        foreach (var deviceName in deviceNames)
        {
            RestoreDevice(deviceName);
        }
    }

    private void RestoreDevice(string deviceName)
    {
        if (!_originalRamps.TryGetValue(deviceName, out var original))
        {
            _lastAppliedRamps.Remove(deviceName);
            return;
        }

        if (_accessor.TrySet(deviceName, original))
        {
            _originalRamps.Remove(deviceName);
            _lastAppliedRamps.Remove(deviceName);
            _neutralizedUnknownDevices.Remove(deviceName);
            _recoveryStore?.Remove(deviceName);
        }
    }

    private void RestoreRecoveredRamps()
    {
        if (_recoveryStore is null)
        {
            return;
        }

        foreach (var entry in _recoveryStore.Load())
        {
            var ramp = new GammaRamp(entry.Values);
            if (_accessor.TrySet(entry.DeviceName, ramp))
            {
                _recoveryStore.Remove(entry.DeviceName);
            }
        }
    }

    private bool ShouldRefreshGamma(IReadOnlyList<string> targetDevices, NightColorState state)
    {
        if (_lastAppliedRamps.Count == 0 ||
            !targetDevices.SequenceEqual(_lastTargetDevices, StringComparer.OrdinalIgnoreCase) ||
            _lastTemperatureKelvin is null ||
            _lastBrightness is null)
        {
            return true;
        }

        if (Math.Abs(state.TemperatureKelvin - _lastTemperatureKelvin.Value) > 15)
        {
            return true;
        }

        if (Math.Abs(state.Brightness - _lastBrightness.Value) > 0.005)
        {
            return true;
        }

        return DateTimeOffset.UtcNow - _lastUpdateAt >= GammaPollingInterval;
    }

    private static MonitorDescriptor[] GammaRampTargets(IReadOnlyList<MonitorDescriptor> monitors) =>
        monitors
            .Where(monitor =>
                monitor.IsActive &&
                !string.IsNullOrWhiteSpace(monitor.DeviceName) &&
                !monitor.AdvancedColor.RequiresConservativeHandling)
            .ToArray();
}

internal interface IDisplayGammaRampAccessor
{
    bool TryGet(string deviceName, out GammaRamp ramp);

    bool TrySet(string deviceName, GammaRamp ramp);
}

internal sealed class WindowsDisplayGammaRampAccessor : IDisplayGammaRampAccessor
{
    public bool TryGet(string deviceName, out GammaRamp ramp)
    {
        using var deviceContext = DisplayDeviceContext.TryCreate(deviceName);
        if (deviceContext is not null)
        {
            return deviceContext.TryGetGammaRamp(out ramp);
        }

        ramp = GammaRamp.Identity;
        return false;
    }

    public bool TrySet(string deviceName, GammaRamp ramp)
    {
        using var deviceContext = DisplayDeviceContext.TryCreate(deviceName);
        return deviceContext?.TrySetGammaRamp(ramp) == true;
    }
}

public static class NightColorGammaRamp
{
    public static double RedMultiplier(int kelvin)
    {
        var temperature = Math.Clamp(kelvin, 1000, 40000);
        return temperature > 6600
            ? Clamp(Math.Pow((temperature / 100.0) - 60, -0.1332047592) * 329.698727446 / 255.0)
            : 1;
    }

    public static double GreenMultiplier(int kelvin)
    {
        var temperature = Math.Clamp(kelvin, 1000, 40000);
        return temperature > 6600
            ? Clamp(Math.Pow((temperature / 100.0) - 60, -0.0755148492) * 288.1221695283 / 255.0)
            : Clamp(((Math.Log(temperature / 100.0) * 99.4708025861) - 161.1195681661) / 255.0);
    }

    public static double BlueMultiplier(int kelvin)
    {
        var temperature = Math.Clamp(kelvin, 1000, 40000);
        if (temperature >= 6600)
        {
            return 1;
        }

        if (temperature <= 1900)
        {
            return 0;
        }

        return Clamp(((Math.Log((temperature / 100.0) - 10) * 138.5177312231) - 305.0447927307) / 255.0);
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);
}

internal sealed class DisplayDeviceContext : IDisposable
{
    private readonly IntPtr _handle;

    private DisplayDeviceContext(IntPtr handle)
    {
        _handle = handle;
    }

    public static DisplayDeviceContext? TryCreate(string deviceName)
    {
        var handle = NativeMethods.CreateDC(deviceName, deviceName, null, IntPtr.Zero);
        return handle == IntPtr.Zero ? null : new DisplayDeviceContext(handle);
    }

    public bool TryGetGammaRamp(out GammaRamp ramp)
    {
        ramp = GammaRamp.Identity;
        var buffer = new ushort[GammaRamp.ValueCount];
        if (!NativeMethods.GetDeviceGammaRamp(_handle, buffer))
        {
            return false;
        }

        ramp = new GammaRamp(buffer);
        return true;
    }

    public bool TrySetGammaRamp(GammaRamp ramp) =>
        NativeMethods.SetDeviceGammaRamp(_handle, ramp.Values);

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.DeleteDC(_handle);
        }
    }
}

internal sealed class GammaRamp : IEquatable<GammaRamp>
{
    public const int ChannelLength = 256;
    public const int ValueCount = ChannelLength * 3;

    public GammaRamp(ushort[] values)
    {
        if (values.Length != ValueCount)
        {
            throw new ArgumentException("Gamma ramp must contain 768 values.", nameof(values));
        }

        Values = values.ToArray();
    }

    public ushort[] Values { get; }

    public static GammaRamp Identity { get; } = FromMultipliers(1, 1, 1, offset: 0);

    public static GammaRamp FromMultipliers(double red, double green, double blue, int offset)
    {
        var values = new ushort[ValueCount];
        FillChannel(values, 0, red);
        FillChannel(values, ChannelLength, green);
        FillChannel(values, ChannelLength * 2, blue);
        var clampedOffset = Math.Clamp(offset, 0, 4);
        values[ChannelLength - 1] = AddOffset(values[ChannelLength - 1], clampedOffset);
        values[(ChannelLength * 2) - 1] = AddOffset(values[(ChannelLength * 2) - 1], clampedOffset);
        values[ValueCount - 1] = AddOffset(values[ValueCount - 1], clampedOffset);
        return new GammaRamp(values);
    }

    public bool Equals(GammaRamp? other) =>
        other is not null && Values.SequenceEqual(other.Values);

    public override bool Equals(object? obj) =>
        obj is GammaRamp other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        for (var index = 0; index < Values.Length; index += 64)
        {
            hash.Add(Values[index]);
        }

        hash.Add(Values[^1]);
        return hash.ToHashCode();
    }

    private static void FillChannel(ushort[] values, int offset, double multiplier)
    {
        var clamped = Math.Clamp(multiplier, 0, 1);
        for (var index = 0; index < ChannelLength; index++)
        {
            values[offset + index] = (ushort)Math.Clamp(Math.Round(index * 257.0 * clamped), 0, ushort.MaxValue);
        }
    }

    private static ushort AddOffset(ushort value, int offset) =>
        (ushort)Math.Clamp(value + offset, 0, ushort.MaxValue);
}
