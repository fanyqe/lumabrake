using LumaBrake.Domain.Color;
using LumaBrake.Domain.Rules;

namespace LumaBrake.Domain.Tests;

public sealed class NightColorEngineTests
{
    [Fact]
    public void Evaluate_ManualModeUsesManualTemperatureAndBrightness()
    {
        var state = NightColorEngine.Evaluate(
            new NightColorOptions
            {
                Enabled = true,
                ScheduleMode = NightColorScheduleMode.Manual,
                ManualTemperatureKelvin = 3400,
                Brightness = 0.80
            },
            new DateTimeOffset(2026, 7, 28, 23, 0, 0, TimeSpan.FromHours(3)));

        Assert.True(state.Enabled);
        Assert.Equal("manual", state.Reason);
        Assert.Equal(3400, state.TemperatureKelvin);
        Assert.Equal(0.20, state.DimmingOpacity, precision: 12);
        Assert.Equal(1, state.NightProgress, precision: 12);
        Assert.True(state.Tint.Enabled);
    }

    [Fact]
    public void Evaluate_CustomScheduleSmoothsAfterStart()
    {
        var state = NightColorEngine.Evaluate(
            new NightColorOptions
            {
                Enabled = true,
                ScheduleMode = NightColorScheduleMode.Custom,
                DayTemperatureKelvin = 6500,
                NightTemperatureKelvin = 3500,
                Brightness = 0.80,
                CustomStartMinuteOfDay = 22 * 60,
                CustomEndMinuteOfDay = 7 * 60,
                TransitionDuration = TimeSpan.FromMinutes(60)
            },
            new DateTimeOffset(2026, 7, 28, 22, 30, 0, TimeSpan.FromHours(3)));

        Assert.True(state.Enabled);
        Assert.Equal("custom", state.Reason);
        Assert.Equal(0.7071067811865475, state.NightProgress, precision: 12);
        Assert.Equal(4379, state.TemperatureKelvin);
        Assert.Equal(0.1414213562373095, state.DimmingOpacity, precision: 12);
    }

    [Fact]
    public void Evaluate_CustomScheduleSmoothsBeforeWake()
    {
        var state = NightColorEngine.Evaluate(
            new NightColorOptions
            {
                Enabled = true,
                ScheduleMode = NightColorScheduleMode.Custom,
                DayTemperatureKelvin = 6500,
                NightTemperatureKelvin = 3500,
                Brightness = 0.80,
                CustomStartMinuteOfDay = 22 * 60,
                CustomEndMinuteOfDay = 7 * 60,
                TransitionDuration = TimeSpan.FromMinutes(60)
            },
            new DateTimeOffset(2026, 7, 29, 6, 30, 0, TimeSpan.FromHours(3)));

        Assert.True(state.Enabled);
        Assert.Equal(0.7071067811865476, state.NightProgress, precision: 12);
        Assert.Equal(4379, state.TemperatureKelvin);
    }

    [Fact]
    public void Evaluate_CustomScheduleIsBackToDayAtWakeTime()
    {
        var state = NightColorEngine.Evaluate(
            new NightColorOptions
            {
                Enabled = true,
                ScheduleMode = NightColorScheduleMode.Custom,
                DayTemperatureKelvin = 6500,
                NightTemperatureKelvin = 3500,
                Brightness = 0.80,
                CustomStartMinuteOfDay = 22 * 60,
                CustomEndMinuteOfDay = 7 * 60,
                TransitionDuration = TimeSpan.FromMinutes(60)
            },
            new DateTimeOffset(2026, 7, 29, 7, 0, 0, TimeSpan.FromHours(3)));

        Assert.False(state.Enabled);
        Assert.Equal("day", state.Reason);
        Assert.Equal(6500, state.TemperatureKelvin);
    }

    [Fact]
    public void Evaluate_TintStrengthSoftensTheEffectiveTemperature()
    {
        var state = NightColorEngine.Evaluate(
            new NightColorOptions
            {
                Enabled = true,
                ScheduleMode = NightColorScheduleMode.Manual,
                ManualTemperatureKelvin = 3400,
                TintStrength = 0.50
            },
            new DateTimeOffset(2026, 7, 28, 23, 0, 0, TimeSpan.FromHours(3)));

        Assert.True(state.Enabled);
        Assert.Equal(4950, state.TemperatureKelvin);
        Assert.True(state.Tint.Enabled);
    }

    [Fact]
    public void Evaluate_SunriseSunsetUsesConfiguredLocation()
    {
        var options = new NightColorOptions
        {
            Enabled = true,
            ScheduleMode = NightColorScheduleMode.SunriseSunset,
            Latitude = 41.0082,
            Longitude = 28.9784,
            DayTemperatureKelvin = 6500,
            NightTemperatureKelvin = 3400,
            Brightness = 0.80
        };

        var daytime = NightColorEngine.Evaluate(
            options,
            new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.FromHours(3)));
        var nighttime = NightColorEngine.Evaluate(
            options,
            new DateTimeOffset(2026, 7, 28, 23, 0, 0, TimeSpan.FromHours(3)));

        Assert.False(daytime.Enabled);
        Assert.True(nighttime.Enabled);
        Assert.Equal("sunrise-sunset", nighttime.Reason);
    }

    [Fact]
    public void TryGetSunCycle_ReturnsResolvedCitySunriseAndSunsetForUiPreview()
    {
        var options = new NightColorOptions
        {
            Enabled = true,
            ScheduleMode = NightColorScheduleMode.SunriseSunset,
            LocationName = "Izmir",
            Latitude = 0,
            Longitude = 0,
            CustomStartMinuteOfDay = 22 * 60,
            CustomEndMinuteOfDay = 7 * 60
        };

        var resolved = NightColorEngine.TryGetSunCycle(
            options,
            new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.FromHours(3)),
            out var cycle);

        Assert.True(resolved);
        Assert.InRange(cycle.SunriseMinuteOfDay, 0, 12 * 60);
        Assert.InRange(cycle.SunsetMinuteOfDay, 12 * 60, 1439);
        Assert.NotEqual(cycle.SunriseMinuteOfDay, cycle.SunsetMinuteOfDay);
    }

    [Fact]
    public void Evaluate_PauseAndExceptionsDisableNightColor()
    {
        var timestamp = new DateTimeOffset(2026, 7, 28, 23, 0, 0, TimeSpan.FromHours(3));
        var options = new NightColorOptions
        {
            Enabled = true,
            ScheduleMode = NightColorScheduleMode.Manual,
            PausedUntilUtc = timestamp.ToUniversalTime().AddMinutes(10),
            ExcludedProcessNames = ["vlc"],
            ExcludedWindowTitleContains = ["movie"]
        };

        var paused = NightColorEngine.Evaluate(options, timestamp);
        var processExcluded = NightColorEngine.Evaluate(
            options with { PausedUntilUtc = null },
            timestamp,
            new ForegroundApplicationInfo("vlc.exe", "Video"));
        var titleExcluded = NightColorEngine.Evaluate(
            options with { PausedUntilUtc = null },
            timestamp,
            new ForegroundApplicationInfo("browser", "Movie night"));
        var fullScreenPaused = NightColorEngine.Evaluate(
            options with
            {
                PausedUntilUtc = null,
                PauseInFullScreenApplications = true
            },
            timestamp,
            new ForegroundApplicationInfo("game", "Fullscreen game", IsFullScreen: true),
            fullScreenApplicationActive: true);

        Assert.False(paused.Enabled);
        Assert.Equal("paused", paused.Reason);
        Assert.False(processExcluded.Enabled);
        Assert.Equal("application-exception", processExcluded.Reason);
        Assert.False(titleExcluded.Enabled);
        Assert.Equal("application-exception", titleExcluded.Reason);
        Assert.False(fullScreenPaused.Enabled);
        Assert.Equal("fullscreen-paused", fullScreenPaused.Reason);
    }

    [Fact]
    public void Clamp_NormalizesUnsafeInput()
    {
        var options = new NightColorOptions
        {
            ScheduleMode = (NightColorScheduleMode)99,
            TransitionMode = (NightColorTransitionMode)99,
            DayTemperatureKelvin = 99999,
            NightTemperatureKelvin = -1,
            ManualTemperatureKelvin = 0,
            Brightness = -10,
            TintStrength = 9,
            CustomStartMinuteOfDay = -1,
            CustomEndMinuteOfDay = 99999,
            Latitude = 120,
            Longitude = -999,
            TransitionDuration = TimeSpan.FromDays(1),
            ExcludedProcessNames = [" chrome ", "", "CHROME"],
            ExcludedWindowTitleContains = [" video ", "VIDEO"]
        }.Clamp();

        Assert.Equal(NightColorScheduleMode.Manual, options.ScheduleMode);
        Assert.Equal(NightColorTransitionMode.Smooth, options.TransitionMode);
        Assert.Equal(12000, options.DayTemperatureKelvin);
        Assert.Equal(1000, options.NightTemperatureKelvin);
        Assert.Equal(1000, options.ManualTemperatureKelvin);
        Assert.Equal(0.20, options.Brightness, precision: 12);
        Assert.Equal(1, options.TintStrength, precision: 12);
        Assert.Equal(0, options.CustomStartMinuteOfDay);
        Assert.Equal(1439, options.CustomEndMinuteOfDay);
        Assert.Equal(89, options.Latitude, precision: 12);
        Assert.Equal(-180, options.Longitude, precision: 12);
        Assert.Equal(TimeSpan.FromHours(6), options.TransitionDuration);
        Assert.Equal(["chrome"], options.ExcludedProcessNames);
        Assert.Equal(["video"], options.ExcludedWindowTitleContains);
    }

    [Fact]
    public void Clamp_UsesComfortableMinimumForSmoothTransitions()
    {
        var smooth = new NightColorOptions
        {
            TransitionMode = NightColorTransitionMode.Smooth,
            TransitionDuration = TimeSpan.FromMinutes(5)
        }.Clamp();
        var instant = new NightColorOptions
        {
            TransitionMode = NightColorTransitionMode.Instant,
            TransitionDuration = TimeSpan.FromMinutes(5)
        }.Clamp();

        Assert.Equal(TimeSpan.FromMinutes(45), smooth.TransitionDuration);
        Assert.Equal(TimeSpan.FromMinutes(5), instant.TransitionDuration);
    }

    [Fact]
    public void Evaluate_CustomScheduleAvoidsHarshMinuteToMinuteJumps()
    {
        var options = new NightColorOptions
        {
            Enabled = true,
            ScheduleMode = NightColorScheduleMode.Custom,
            DayTemperatureKelvin = 6500,
            NightTemperatureKelvin = 3500,
            Brightness = 0.80,
            CustomStartMinuteOfDay = 22 * 60,
            CustomEndMinuteOfDay = 7 * 60,
            TransitionDuration = TimeSpan.FromMinutes(60)
        };
        var start = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.FromHours(3));

        for (var minute = 0; minute < 1440; minute++)
        {
            var current = NightColorEngine.Evaluate(options, start.AddMinutes(minute));
            var next = NightColorEngine.Evaluate(options, start.AddMinutes(minute + 1));

            Assert.True(
                Math.Abs(current.TemperatureKelvin - next.TemperatureKelvin) < 90,
                $"Harsh temperature jump at minute {minute}: {current.TemperatureKelvin}K -> {next.TemperatureKelvin}K.");
            Assert.True(
                Math.Abs(current.Brightness - next.Brightness) < 0.01,
                $"Harsh brightness jump at minute {minute}: {current.Brightness} -> {next.Brightness}.");
        }
    }

    [Theory]
    [InlineData("Izmir")]
    [InlineData("\u0130zmir")]
    [InlineData("izmir")]
    public void Clamp_ResolvesCityNameWithoutUserFacingCoordinates(string locationName)
    {
        var options = new NightColorOptions
        {
            LocationName = locationName,
            Latitude = 0,
            Longitude = 0
        }.Clamp();

        Assert.Equal("Izmir", options.LocationName);
        Assert.Equal(38.4237, options.Latitude, precision: 4);
        Assert.Equal(27.1428, options.Longitude, precision: 4);
    }

    [Fact]
    public void Clamp_KeepsCoordinatesWhenCityNameIsEmptyForCliCompatibility()
    {
        var options = new NightColorOptions
        {
            LocationName = "",
            Latitude = 40.7128,
            Longitude = -74.0060
        }.Clamp();

        Assert.Equal("", options.LocationName);
        Assert.Equal(40.7128, options.Latitude, precision: 4);
        Assert.Equal(-74.0060, options.Longitude, precision: 4);
    }
}
