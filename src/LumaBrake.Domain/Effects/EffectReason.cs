namespace LumaBrake.Domain.Effects;

public enum EffectReason
{
    None = 0,
    Manual = 10,
    BrightnessSpike = 20,
    RepetitiveFlash = 30,
    StaticShade = 40,
    InactiveMonitor = 45,
    AdaptiveContent = 50,
    FocusDimming = 55,
    Scheduled = 60
}
