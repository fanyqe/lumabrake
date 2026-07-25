using LumaBrake.Domain.Frames;

namespace LumaBrake.Capture;

public sealed record CapturedFrame(byte[] BgraPixels, FrameMetadata Metadata);
