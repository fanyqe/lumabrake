namespace LumaBrake.Domain.Analysis;

public sealed record ProtectionTuningOptions
{
    public ProtectionStrengthPreset Strength { get; init; } = ProtectionStrengthPreset.Balanced;

    public ProtectionResponsePreset Response { get; init; } = ProtectionResponsePreset.Balanced;

    public LocalProtectionPreset LocalProtection { get; init; } = LocalProtectionPreset.Balanced;

    public LocalMaskFitPreset LocalMaskFit { get; init; } = LocalMaskFitPreset.Precise;

    public WhitePageProtectionPreset WhitePageProtection { get; init; } = WhitePageProtectionPreset.Normal;

    public StaticShadePreset StaticShadeProfile { get; init; } = StaticShadePreset.OledBalanced;

    public ProtectionTuningOptions Clamp() =>
        this with
        {
            Strength = Enum.IsDefined(typeof(ProtectionStrengthPreset), Strength)
                ? Strength
                : ProtectionStrengthPreset.Balanced,
            Response = Enum.IsDefined(typeof(ProtectionResponsePreset), Response)
                ? Response
                : ProtectionResponsePreset.Balanced,
            LocalProtection = Enum.IsDefined(typeof(LocalProtectionPreset), LocalProtection)
                ? LocalProtection
                : LocalProtectionPreset.Balanced,
            LocalMaskFit = Enum.IsDefined(typeof(LocalMaskFitPreset), LocalMaskFit)
                ? LocalMaskFit
                : LocalMaskFitPreset.Precise,
            WhitePageProtection = Enum.IsDefined(typeof(WhitePageProtectionPreset), WhitePageProtection)
                ? WhitePageProtection
                : WhitePageProtectionPreset.Normal,
            StaticShadeProfile = Enum.IsDefined(typeof(StaticShadePreset), StaticShadeProfile)
                ? StaticShadeProfile
                : StaticShadePreset.OledBalanced
        };
}

public enum ProtectionStrengthPreset
{
    Gentle,
    Balanced,
    Strong,
    Maximum
}

public enum ProtectionResponsePreset
{
    Calm,
    Balanced,
    Fast
}

public enum LocalProtectionPreset
{
    Calm,
    Balanced,
    Strong,
    Aggressive
}

public enum LocalMaskFitPreset
{
    Precise,
    Balanced,
    Expanded
}

public enum WhitePageProtectionPreset
{
    Light,
    Normal,
    Strong
}
