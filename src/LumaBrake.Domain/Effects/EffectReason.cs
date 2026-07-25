namespace LumaBrake.Domain.Effects;

public enum EffectReason
{
    None = 0,
    Manual = 10,
    BrightnessSpike = 20,
    RepetitiveFlash = 30,
    StaticShade = 40,
    AdaptiveContent = 50
}
