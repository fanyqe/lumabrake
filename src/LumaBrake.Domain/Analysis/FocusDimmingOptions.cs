namespace LumaBrake.Domain.Analysis;

public sealed record FocusDimmingOptions
{
    public bool Enabled { get; init; }

    public double Opacity { get; init; } = 0.16;

    public TimeSpan Hold { get; init; } = TimeSpan.FromMilliseconds(150);

    public TimeSpan Release { get; init; } = TimeSpan.FromMilliseconds(500);

    public FocusDimmingOptions Clamp() =>
        this with
        {
            Opacity = Math.Clamp(Opacity, 0, 0.60),
            Hold = Hold < TimeSpan.Zero ? TimeSpan.Zero : Hold,
            Release = Release < TimeSpan.Zero ? TimeSpan.Zero : Release
        };
}
