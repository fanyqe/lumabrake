namespace LumaBrake.Domain.Frames;

public sealed record FrameMetadata(
    DateTimeOffset CapturedAt,
    int Width,
    int Height,
    string Source,
    string ColorSpace = "sRGB");
