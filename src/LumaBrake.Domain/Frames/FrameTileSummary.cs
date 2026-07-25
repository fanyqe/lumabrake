namespace LumaBrake.Domain.Frames;

public sealed record FrameTileSummary(
    int Columns,
    int Rows,
    IReadOnlyList<TileLuminanceStats> Tiles);
