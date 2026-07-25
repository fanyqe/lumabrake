using System.Globalization;
using System.Text;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Rules;

namespace LumaBrake.Domain.Color;

public sealed record NightColorOptions
{
    public bool Enabled { get; init; }

    public NightColorScheduleMode ScheduleMode { get; init; } = NightColorScheduleMode.Manual;

    public NightColorTransitionMode TransitionMode { get; init; } = NightColorTransitionMode.Smooth;

    public int DayTemperatureKelvin { get; init; } = 6500;

    public int NightTemperatureKelvin { get; init; } = 3400;

    public int ManualTemperatureKelvin { get; init; } = 4200;

    public double Brightness { get; init; } = 1.0;

    public double TintStrength { get; init; } = 1.0;

    public int CustomStartMinuteOfDay { get; init; } = 22 * 60;

    public int CustomEndMinuteOfDay { get; init; } = 7 * 60;

    public string LocationName { get; init; } = string.Empty;

    public double Latitude { get; init; } = 41.0082;

    public double Longitude { get; init; } = 28.9784;

    public TimeSpan TransitionDuration { get; init; } = TimeSpan.FromMinutes(60);

    public DateTimeOffset? PausedUntilUtc { get; init; }

    public bool PauseInFullScreenApplications { get; init; }

    public string[] ExcludedProcessNames { get; init; } = [];

    public string[] ExcludedWindowTitleContains { get; init; } = [];

    public NightColorOptions Clamp()
    {
        var normalizedLocationName = NightColorLocationCatalog.NormalizeLocationName(LocationName);
        var latitude = Math.Clamp(Latitude, -89.0, 89.0);
        var longitude = Math.Clamp(Longitude, -180.0, 180.0);
        if (!string.IsNullOrWhiteSpace(normalizedLocationName) &&
            NightColorLocationCatalog.TryResolve(normalizedLocationName, out var location))
        {
            normalizedLocationName = location.Name;
            latitude = location.Latitude;
            longitude = location.Longitude;
        }

        return this with
        {
            ScheduleMode = Enum.IsDefined(typeof(NightColorScheduleMode), ScheduleMode)
                ? ScheduleMode
                : NightColorScheduleMode.Manual,
            TransitionMode = Enum.IsDefined(typeof(NightColorTransitionMode), TransitionMode)
                ? TransitionMode
                : NightColorTransitionMode.Smooth,
            DayTemperatureKelvin = ClampKelvin(DayTemperatureKelvin),
            NightTemperatureKelvin = ClampKelvin(NightTemperatureKelvin),
            ManualTemperatureKelvin = ClampKelvin(ManualTemperatureKelvin),
            Brightness = Math.Clamp(Brightness, 0.20, 1.0),
            TintStrength = Math.Clamp(TintStrength, 0, 1.0),
            CustomStartMinuteOfDay = ClampMinute(CustomStartMinuteOfDay),
            CustomEndMinuteOfDay = ClampMinute(CustomEndMinuteOfDay),
            LocationName = normalizedLocationName,
            Latitude = latitude,
            Longitude = longitude,
            TransitionDuration = ClampTransitionDuration(TransitionDuration, TransitionMode),
            ExcludedProcessNames = NormalizeTokens(ExcludedProcessNames),
            ExcludedWindowTitleContains = NormalizeTokens(ExcludedWindowTitleContains)
        };
    }

    private static int ClampKelvin(int kelvin) => Math.Clamp(kelvin, 1000, 12000);

    private static int ClampMinute(int minuteOfDay) => Math.Clamp(minuteOfDay, 0, 1439);

    private static TimeSpan ClampTransitionDuration(TimeSpan duration, NightColorTransitionMode transitionMode)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var maximum = duration > TimeSpan.FromHours(6) ? TimeSpan.FromHours(6) : duration;
        if (transitionMode == NightColorTransitionMode.Instant)
        {
            return maximum;
        }

        return maximum < TimeSpan.FromMinutes(45) ? TimeSpan.FromMinutes(45) : maximum;
    }

    private static string[] NormalizeTokens(IEnumerable<string>? values) =>
        (values ?? [])
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

public enum NightColorScheduleMode
{
    Manual,
    Custom,
    SunriseSunset
}

public enum NightColorTransitionMode
{
    Instant,
    Smooth
}

public sealed record NightColorLocation(string Name, double Latitude, double Longitude);

public static class NightColorLocationCatalog
{
    private static readonly IReadOnlyDictionary<string, NightColorLocation> Locations = BuildLocations();

    public static string NormalizeLocationName(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized;
    }

    public static bool TryResolve(string? value, out NightColorLocation location)
    {
        var key = SearchKey(value);
        if (key is not null && Locations.TryGetValue(key, out location!))
        {
            return true;
        }

        location = null!;
        return false;
    }

    private static IReadOnlyDictionary<string, NightColorLocation> BuildLocations()
    {
        var locations = new Dictionary<string, NightColorLocation>(StringComparer.OrdinalIgnoreCase);
        Add(locations, new NightColorLocation("Istanbul", 41.0082, 28.9784), "istanbul", "ist");
        Add(locations, new NightColorLocation("Izmir", 38.4237, 27.1428), "izmir");
        Add(locations, new NightColorLocation("Ankara", 39.9334, 32.8597), "ankara");
        Add(locations, new NightColorLocation("Antalya", 36.8969, 30.7133), "antalya");
        Add(locations, new NightColorLocation("Bursa", 40.1828, 29.0663), "bursa");
        Add(locations, new NightColorLocation("Adana", 36.9914, 35.3308), "adana");
        Add(locations, new NightColorLocation("Konya", 37.8714, 32.4846), "konya");
        Add(locations, new NightColorLocation("Gaziantep", 37.0662, 37.3833), "gaziantep", "antep");
        Add(locations, new NightColorLocation("Mersin", 36.8121, 34.6415), "mersin");
        Add(locations, new NightColorLocation("Diyarbakir", 37.9144, 40.2306), "diyarbakir");
        Add(locations, new NightColorLocation("Kayseri", 38.7205, 35.4826), "kayseri");
        Add(locations, new NightColorLocation("Eskisehir", 39.7667, 30.5256), "eskisehir");
        Add(locations, new NightColorLocation("Samsun", 41.2867, 36.33), "samsun");
        Add(locations, new NightColorLocation("Trabzon", 41.0027, 39.7168), "trabzon");
        Add(locations, new NightColorLocation("Erzurum", 39.9000, 41.2700), "erzurum");
        Add(locations, new NightColorLocation("Kocaeli", 40.7654, 29.9408), "kocaeli", "izmit");
        Add(locations, new NightColorLocation("Sakarya", 40.7731, 30.3948), "sakarya", "adapazari");
        Add(locations, new NightColorLocation("Balikesir", 39.6484, 27.8826), "balikesir");
        Add(locations, new NightColorLocation("Aydin", 37.8450, 27.8396), "aydin");
        Add(locations, new NightColorLocation("Mugla", 37.2153, 28.3636), "mugla");
        Add(locations, new NightColorLocation("Denizli", 37.7765, 29.0864), "denizli");
        Add(locations, new NightColorLocation("Canakkale", 40.1553, 26.4142), "canakkale");
        Add(locations, new NightColorLocation("Edirne", 41.6771, 26.5557), "edirne");
        Add(locations, new NightColorLocation("London", 51.5074, -0.1278), "london", "londra");
        Add(locations, new NightColorLocation("Berlin", 52.52, 13.405), "berlin");
        Add(locations, new NightColorLocation("New York", 40.7128, -74.0060), "new york", "nyc");
        return locations;
    }

    private static void Add(
        Dictionary<string, NightColorLocation> locations,
        NightColorLocation location,
        params string[] aliases)
    {
        foreach (var alias in aliases.Append(location.Name))
        {
            var key = SearchKey(alias);
            if (key is not null)
            {
                locations[key] = location;
            }
        }
    }

    private static string? SearchKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var lowered = value.Trim().ToLowerInvariant().Replace('\u0131', 'i');
        var decomposed = lowered.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString()
            .Normalize(NormalizationForm.FormC)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal)
            .Replace("_", " ", StringComparison.Ordinal)
            .Trim();
    }
}

public sealed record NightColorState(
    bool Enabled,
    int TemperatureKelvin,
    double Brightness,
    double NightProgress,
    TintEffect Tint,
    double DimmingOpacity,
    string Reason)
{
    public static NightColorState Disabled { get; } = new(
        false,
        6500,
        1.0,
        0,
        TintEffect.Disabled,
        0,
        "disabled");
}

public sealed record NightColorSunCycle(int SunriseMinuteOfDay, int SunsetMinuteOfDay);

public static class NightColorEngine
{
    private const double WarmTintMaximumOpacity = 0.24;
    private const double WarmTintMinimumTemperature = 1000;
    private const double WarmTintNeutralTemperature = 6500;
    private const double DegreesToRadians = Math.PI / 180.0;
    private const double RadiansToDegrees = 180.0 / Math.PI;

    public static NightColorState Evaluate(
        NightColorOptions options,
        DateTimeOffset localTimestamp,
        ForegroundApplicationInfo? foregroundApplication = null,
        bool fullScreenApplicationActive = false)
    {
        var clamped = options.Clamp();
        if (!clamped.Enabled)
        {
            return NightColorState.Disabled;
        }

        var nowUtc = localTimestamp.ToUniversalTime();
        if (clamped.PausedUntilUtc is { } pausedUntil && pausedUntil.ToUniversalTime() > nowUtc)
        {
            return NightColorState.Disabled with { Reason = "paused" };
        }

        if (fullScreenApplicationActive && clamped.PauseInFullScreenApplications)
        {
            return NightColorState.Disabled with { Reason = "fullscreen-paused" };
        }

        if (MatchesException(clamped, foregroundApplication))
        {
            return NightColorState.Disabled with { Reason = "application-exception" };
        }

        var progress = clamped.ScheduleMode switch
        {
            NightColorScheduleMode.Manual => 1.0,
            NightColorScheduleMode.SunriseSunset => SunriseSunsetNightProgress(clamped, localTimestamp),
            _ => CustomNightProgress(
                clamped.CustomStartMinuteOfDay,
                clamped.CustomEndMinuteOfDay,
                localTimestamp.TimeOfDay,
                clamped.TransitionMode,
                clamped.TransitionDuration)
        };

        var scheduledTemperature = clamped.ScheduleMode == NightColorScheduleMode.Manual
            ? clamped.ManualTemperatureKelvin
            : InterpolateKelvin(clamped.DayTemperatureKelvin, clamped.NightTemperatureKelvin, progress);
        var temperature = InterpolateKelvin(
            (int)WarmTintNeutralTemperature,
            scheduledTemperature,
            clamped.TintStrength);
        var tint = TintForTemperature(temperature, progress);
        var dimmingOpacity = progress <= 0 ? 0 : Math.Clamp(1 - clamped.Brightness, 0, 0.80) * progress;

        if (!tint.Enabled && dimmingOpacity <= 0.001)
        {
            return NightColorState.Disabled with
            {
                TemperatureKelvin = temperature,
                Brightness = 1 - dimmingOpacity,
                NightProgress = progress,
                Reason = "day"
            };
        }

        return new NightColorState(
            true,
            temperature,
            Math.Clamp(1 - dimmingOpacity, 0.20, 1.0),
            progress,
            tint,
            dimmingOpacity,
            ReasonForScheduleMode(clamped.ScheduleMode));
    }

    public static TintEffect TintForTemperature(int kelvin, double intensity = 1.0)
    {
        var clampedKelvin = Math.Clamp(kelvin, 1000, 12000);
        var warmth = Math.Clamp(
            (WarmTintNeutralTemperature - clampedKelvin) / (WarmTintNeutralTemperature - WarmTintMinimumTemperature),
            0,
            1);
        var opacity = WarmTintMaximumOpacity * SmoothStep(warmth) * Math.Clamp(intensity, 0, 1);
        var color = ColorTemperatureRgb(clampedKelvin);

        return opacity <= 0.001
            ? TintEffect.Disabled
            : new TintEffect(true, color.Red, color.Green, color.Blue, opacity).Clamp();
    }

    public static bool TryGetSunCycle(
        NightColorOptions options,
        DateTimeOffset localTimestamp,
        out NightColorSunCycle sunCycle)
    {
        var clamped = options.Clamp();
        if (TrySunriseSunsetMinutes(
                DateOnly.FromDateTime(localTimestamp.Date),
                clamped.Latitude,
                clamped.Longitude,
                localTimestamp.Offset,
                out var sunrise,
                out var sunset))
        {
            sunCycle = new NightColorSunCycle(sunrise, sunset);
            return true;
        }

        sunCycle = new NightColorSunCycle(clamped.CustomEndMinuteOfDay, clamped.CustomStartMinuteOfDay);
        return false;
    }

    private static (byte Red, byte Green, byte Blue) ColorTemperatureRgb(int kelvin)
    {
        var temperature = Math.Clamp(kelvin, 1000, 40000) / 100.0;
        var red = temperature <= 66
            ? 255
            : 329.698727446 * Math.Pow(temperature - 60, -0.1332047592);
        var green = temperature <= 66
            ? 99.4708025861 * Math.Log(temperature) - 161.1195681661
            : 288.1221695283 * Math.Pow(temperature - 60, -0.0755148492);
        var blue = temperature >= 66
            ? 255
            : temperature <= 19
                ? 0
                : 138.5177312231 * Math.Log(temperature - 10) - 305.0447927307;

        return (ToByte(red), ToByte(green), ToByte(blue));
    }

    private static double SmoothStep(double value)
    {
        var clamped = Math.Clamp(value, 0, 1);
        return clamped * clamped * (3 - (2 * clamped));
    }

    private static byte ToByte(double value) =>
        (byte)Math.Clamp(Math.Round(value), byte.MinValue, byte.MaxValue);

    private static double SunriseSunsetNightProgress(NightColorOptions options, DateTimeOffset localTimestamp)
    {
        if (!TrySunriseSunsetMinutes(
            DateOnly.FromDateTime(localTimestamp.Date),
            options.Latitude,
            options.Longitude,
            localTimestamp.Offset,
            out var sunrise,
            out var sunset))
        {
            return CustomNightProgress(
                options.CustomStartMinuteOfDay,
                options.CustomEndMinuteOfDay,
                localTimestamp.TimeOfDay,
                options.TransitionMode,
                options.TransitionDuration);
        }

        return CustomNightProgress(sunset, sunrise, localTimestamp.TimeOfDay, options.TransitionMode, options.TransitionDuration);
    }

    private static double CustomNightProgress(
        int startMinute,
        int endMinute,
        TimeSpan localTime,
        NightColorTransitionMode transitionMode,
        TimeSpan transitionDuration)
    {
        if (startMinute == endMinute)
        {
            return 0;
        }

        var currentMinute = MinuteOfDay(localTime);
        var nightDuration = ForwardMinutes(startMinute, endMinute);
        var sinceStart = ForwardMinutes(startMinute, currentMinute);
        var inNightWindow = sinceStart < nightDuration;
        if (transitionMode == NightColorTransitionMode.Instant || transitionDuration <= TimeSpan.Zero)
        {
            return inNightWindow ? 1 : 0;
        }

        var transitionMinutes = Math.Clamp(
            transitionDuration.TotalMinutes,
            1,
            Math.Max(1, nightDuration / 2.0));
        var sunsetEnd = AddMinutesOfDay(startMinute, (int)Math.Round(transitionMinutes));
        var sunriseStart = AddMinutesOfDay(endMinute, -(int)Math.Round(transitionMinutes));

        if (IsInForwardWindow(startMinute, sunsetEnd, currentMinute))
        {
            return SunsetEase(Math.Clamp(sinceStart / transitionMinutes, 0, 1));
        }

        if (IsInForwardWindow(sunriseStart, endMinute, currentMinute))
        {
            var sinceSunriseStart = ForwardMinutes(sunriseStart, currentMinute);
            return SunriseEase(Math.Clamp(sinceSunriseStart / transitionMinutes, 0, 1));
        }

        return inNightWindow ? 1 : 0;
    }

    private static bool TrySunriseSunsetMinutes(
        DateOnly date,
        double latitude,
        double longitude,
        TimeSpan offset,
        out int sunriseMinute,
        out int sunsetMinute)
    {
        sunriseMinute = 7 * 60;
        sunsetMinute = 22 * 60;

        if (!TrySolarEventMinute(date, latitude, longitude, offset, sunrise: true, out sunriseMinute) ||
            !TrySolarEventMinute(date, latitude, longitude, offset, sunrise: false, out sunsetMinute) ||
            sunriseMinute == sunsetMinute)
        {
            return false;
        }

        return true;
    }

    private static bool TrySolarEventMinute(
        DateOnly date,
        double latitude,
        double longitude,
        TimeSpan offset,
        bool sunrise,
        out int minuteOfDay)
    {
        const double zenith = 90.833;
        var dayOfYear = date.DayOfYear;
        var longitudeHour = longitude / 15.0;
        var approximateTime = dayOfYear + (((sunrise ? 6.0 : 18.0) - longitudeHour) / 24.0);
        var meanAnomaly = (0.9856 * approximateTime) - 3.289;
        var trueLongitude = NormalizeDegrees(
            meanAnomaly +
            (1.916 * Math.Sin(meanAnomaly * DegreesToRadians)) +
            (0.020 * Math.Sin(2 * meanAnomaly * DegreesToRadians)) +
            282.634);
        var rightAscension = Math.Atan(0.91764 * Math.Tan(trueLongitude * DegreesToRadians)) * RadiansToDegrees;
        rightAscension = NormalizeDegrees(rightAscension);
        rightAscension += Math.Floor(trueLongitude / 90.0) * 90.0 - Math.Floor(rightAscension / 90.0) * 90.0;
        rightAscension /= 15.0;

        var sinDeclination = 0.39782 * Math.Sin(trueLongitude * DegreesToRadians);
        var cosDeclination = Math.Cos(Math.Asin(sinDeclination));
        var cosHour = (Math.Cos(zenith * DegreesToRadians) - (sinDeclination * Math.Sin(latitude * DegreesToRadians))) /
            (cosDeclination * Math.Cos(latitude * DegreesToRadians));
        if (cosHour is > 1 or < -1)
        {
            minuteOfDay = 0;
            return false;
        }

        var hourAngle = sunrise ? 360.0 - (Math.Acos(cosHour) * RadiansToDegrees) : Math.Acos(cosHour) * RadiansToDegrees;
        hourAngle /= 15.0;
        var localMeanTime = hourAngle + rightAscension - (0.06571 * approximateTime) - 6.622;
        var utcHour = NormalizeHour(localMeanTime - longitudeHour);
        var localHour = NormalizeHour(utcHour + offset.TotalHours);
        minuteOfDay = Math.Clamp((int)Math.Round(localHour * 60.0), 0, 1439);
        return true;
    }

    private static int InterpolateKelvin(int dayKelvin, int nightKelvin, double progress) =>
        (int)Math.Round(dayKelvin + ((nightKelvin - dayKelvin) * Math.Clamp(progress, 0, 1)));

    private static int AddMinutesOfDay(int minuteOfDay, int offset)
    {
        var normalized = (minuteOfDay + offset) % 1440;
        return normalized < 0 ? normalized + 1440 : normalized;
    }

    private static bool IsInForwardWindow(int startMinute, int endMinute, int currentMinute)
    {
        var duration = ForwardMinutes(startMinute, endMinute);
        return duration > 0 && ForwardMinutes(startMinute, currentMinute) < duration;
    }

    private static double SunsetEase(double progress) =>
        Math.Sin(Math.Clamp(progress, 0, 1) * Math.PI / 2);

    private static double SunriseEase(double progress) =>
        Math.Cos(Math.Clamp(progress, 0, 1) * Math.PI / 2);

    private static int MinuteOfDay(TimeSpan localTime)
    {
        var minutes = (int)Math.Floor(localTime.TotalMinutes);
        var normalized = minutes % 1440;
        return normalized < 0 ? normalized + 1440 : normalized;
    }

    private static int ForwardMinutes(int fromMinute, int toMinute) =>
        (toMinute - fromMinute + 1440) % 1440;

    private static double NormalizeDegrees(double value)
    {
        var normalized = value % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }

    private static double NormalizeHour(double value)
    {
        var normalized = value % 24.0;
        return normalized < 0 ? normalized + 24.0 : normalized;
    }

    private static bool MatchesException(NightColorOptions options, ForegroundApplicationInfo? application)
    {
        if (application is null)
        {
            return false;
        }

        var processName = NormalizeProcessName(application.ProcessName);
        var windowTitle = Normalize(application.WindowTitle);
        return options.ExcludedProcessNames.Any(excluded =>
            string.Equals(NormalizeProcessName(excluded), processName, StringComparison.OrdinalIgnoreCase)) ||
            options.ExcludedWindowTitleContains.Any(excluded =>
                windowTitle?.Contains(excluded, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeProcessName(string? value)
    {
        var normalized = Normalize(value);
        return normalized?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true
            ? normalized[..^4]
            : normalized;
    }

    private static string ReasonForScheduleMode(NightColorScheduleMode mode) =>
        mode switch
        {
            NightColorScheduleMode.Custom => "custom",
            NightColorScheduleMode.SunriseSunset => "sunrise-sunset",
            _ => "manual"
        };
}
