namespace LumaBrake.Domain.Hardware;

public static class HardwareBrightnessSoftwareFallback
{
    public static double ManualDimmingOpacityForBrightness(int requestedBrightness)
    {
        var brightnessRatio = Math.Clamp(requestedBrightness, 0, 100) / 100.0;
        return Math.Clamp(1 - brightnessRatio, 0, 0.90);
    }
}
