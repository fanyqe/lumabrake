using LumaBrake.Domain.Frames;
using LumaBrake.Domain.Monitors;

namespace LumaBrake.Capture;

public sealed record CapturedFrame(byte[] BgraPixels, FrameMetadata Metadata, MonitorDescriptor Monitor);
