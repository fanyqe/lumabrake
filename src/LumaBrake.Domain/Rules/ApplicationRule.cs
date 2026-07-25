namespace LumaBrake.Domain.Rules;

public sealed record ApplicationRule
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public string? ProcessName { get; init; }

    public string? WindowTitleContains { get; init; }

    public bool ExcludeAutomaticEffects { get; init; } = true;

    public bool ExcludeNightColor { get; init; }

    public ApplicationRule Clamp()
    {
        var processName = Normalize(ProcessName);
        var windowTitleContains = Normalize(WindowTitleContains);
        var id = Normalize(Id) ?? processName ?? windowTitleContains ?? string.Empty;

        return this with
        {
            Id = id,
            Name = Normalize(Name) ?? id,
            ProcessName = processName,
            WindowTitleContains = windowTitleContains
        };
    }

    public bool IsMatch(ForegroundApplicationInfo? application)
    {
        var rule = Clamp();
        if (!rule.Enabled || !rule.ExcludeAutomaticEffects || application is null)
        {
            return false;
        }

        return rule.MatchesApplication(application);
    }

    public bool IsNightColorMatch(ForegroundApplicationInfo? application)
    {
        var rule = Clamp();
        if (!rule.Enabled || !rule.ExcludeNightColor || application is null)
        {
            return false;
        }

        return rule.MatchesApplication(application);
    }

    private bool MatchesApplication(ForegroundApplicationInfo application)
    {
        var rule = Clamp();
        var processName = Normalize(application.ProcessName);
        var windowTitle = Normalize(application.WindowTitle);
        var hasProcessCriterion = !string.IsNullOrWhiteSpace(rule.ProcessName);
        var hasWindowTitleCriterion = !string.IsNullOrWhiteSpace(rule.WindowTitleContains);
        if (!hasProcessCriterion && !hasWindowTitleCriterion)
        {
            return false;
        }

        if (hasProcessCriterion &&
            !string.Equals(
                NormalizeProcessName(processName),
                NormalizeProcessName(rule.ProcessName),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !hasWindowTitleCriterion ||
            (windowTitle?.Contains(rule.WindowTitleContains!, StringComparison.OrdinalIgnoreCase) == true);
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
}
