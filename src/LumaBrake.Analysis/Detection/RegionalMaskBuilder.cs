using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Frames;
using LumaBrake.Domain.Analysis;

namespace LumaBrake.Analysis.Detection;

internal static class RegionalMaskBuilder
{
    public static RegionalMaskBuildResult Build(
        FrameTileSummary summary,
        IReadOnlyCollection<int> seedTileIndexes,
        RegionalMaskBuildOptions options)
    {
        if (summary.Tiles.Count == 0 || seedTileIndexes.Count == 0)
        {
            return RegionalMaskBuildResult.Empty;
        }

        var activeTiles = NormalizeSeedTiles(summary, seedTileIndexes);
        if (options.IncludeNeighboringBrightTiles)
        {
            ExpandThroughBrightNeighbors(summary, activeTiles, options);
        }

        if (activeTiles.Count == 0 ||
            activeTiles.Count / (double)summary.Tiles.Count > Math.Clamp(options.MaximumTileRatio, 0, 1))
        {
            return RegionalMaskBuildResult.Empty;
        }

        var regions = new List<EffectRegion>();
        var includedTileCount = 0;
        foreach (var component in ConnectedComponents(summary, activeTiles))
        {
            var region = RegionForComponent(summary, component, options);
            if (region is null)
            {
                continue;
            }

            includedTileCount += component.Count;
            regions.Add(region);
        }

        if (regions.Count == 0)
        {
            return RegionalMaskBuildResult.Empty;
        }

        return new RegionalMaskBuildResult(MergeRegions(regions, EffectiveMergeTolerance(options)), includedTileCount);
    }

    public static EffectRegion TileRegion(TileLuminanceStats tile, FrameTileSummary summary)
    {
        var width = 1.0 / summary.Columns;
        var height = 1.0 / summary.Rows;
        return new EffectRegion(tile.Column * width, tile.Row * height, width, height);
    }

    private static HashSet<int> NormalizeSeedTiles(FrameTileSummary summary, IReadOnlyCollection<int> seedTileIndexes)
    {
        var activeTiles = new HashSet<int>();
        foreach (var index in seedTileIndexes)
        {
            if (index >= 0 && index < summary.Tiles.Count)
            {
                activeTiles.Add(index);
            }
        }

        return activeTiles;
    }

    private static void ExpandThroughBrightNeighbors(
        FrameTileSummary summary,
        HashSet<int> activeTiles,
        RegionalMaskBuildOptions options)
    {
        var queue = new Queue<int>(activeTiles);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var neighbor in NeighborIndexes(summary, current))
            {
                if (activeTiles.Contains(neighbor) || !IsExpansionTile(summary.Tiles[neighbor], options))
                {
                    continue;
                }

                activeTiles.Add(neighbor);
                queue.Enqueue(neighbor);
            }
        }
    }

    private static bool IsExpansionTile(TileLuminanceStats tile, RegionalMaskBuildOptions options) =>
        tile.AverageLuminance >= options.NeighborBrightAverageThreshold ||
        tile.Percentile95Luminance >= options.NeighborBrightPercentile95Threshold ||
        tile.BrightPixelRatio >= options.NeighborBrightPixelRatioThreshold ||
        tile.RedDominantPixelRatio >= options.NeighborRedDominantPixelRatioThreshold;

    private static IEnumerable<IReadOnlyList<int>> ConnectedComponents(FrameTileSummary summary, HashSet<int> activeTiles)
    {
        var visited = new HashSet<int>();
        foreach (var seed in activeTiles.OrderBy(index => index))
        {
            if (!visited.Add(seed))
            {
                continue;
            }

            var component = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);

                foreach (var neighbor in NeighborIndexes(summary, current))
                {
                    if (activeTiles.Contains(neighbor) && visited.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            yield return component;
        }
    }

    private static IEnumerable<int> NeighborIndexes(FrameTileSummary summary, int index)
    {
        var tile = summary.Tiles[index];
        for (var row = tile.Row - 1; row <= tile.Row + 1; row++)
        {
            if (row < 0 || row >= summary.Rows)
            {
                continue;
            }

            for (var column = tile.Column - 1; column <= tile.Column + 1; column++)
            {
                if (column < 0 || column >= summary.Columns || (row == tile.Row && column == tile.Column))
                {
                    continue;
                }

                var neighbor = (row * summary.Columns) + column;
                if (neighbor >= 0 && neighbor < summary.Tiles.Count)
                {
                    yield return neighbor;
                }
            }
        }
    }

    private static EffectRegion? RegionForComponent(
        FrameTileSummary summary,
        IReadOnlyList<int> component,
        RegionalMaskBuildOptions options)
    {
        if (component.Count == 1)
        {
            return RegionForSingleTile(summary, component[0], options);
        }

        var tileRegion = ComponentTileRegion(summary, component);
        if (options.MaskFit == LocalMaskFitPreset.Expanded)
        {
            return tileRegion.Pad(options.Padding);
        }

        var signalRegion = SignalRegionForComponent(summary, component, options);
        if (signalRegion is null)
        {
            return tileRegion.Pad(options.Padding);
        }

        var signalAreaRatio = tileRegion.Area <= 0
            ? 1
            : signalRegion.Clamp().Area / tileRegion.Area;
        if (signalAreaRatio < options.MinimumComponentSignalAreaRatio)
        {
            return tileRegion.Pad(options.Padding);
        }

        return FitSignalRegion(signalRegion, tileRegion, options.MaskFit).Pad(options.Padding);
    }

    private static EffectRegion? RegionForSingleTile(
        FrameTileSummary summary,
        int index,
        RegionalMaskBuildOptions options)
    {
        var tile = summary.Tiles[index];
        var tileRegion = TileRegion(tile, summary);
        var preferredRegion = options.PreferredSignalRegion?.Invoke(index, tile) ?? tile.SignalBounds;
        var signalAreaRatio = preferredRegion is null || tileRegion.Area <= 0
            ? 1
            : preferredRegion.Clamp().Area / tileRegion.Area;
        if (preferredRegion is not null &&
            options.TrustPreferredSignalRegion &&
            signalAreaRatio >= options.MinimumSingleTileSignalAreaRatio)
        {
            return FitSignalRegion(preferredRegion, tileRegion, options.MaskFit).Pad(options.Padding);
        }

        if (preferredRegion is null && options.AllowSingleTileWithoutSignal)
        {
            return tileRegion.Pad(options.Padding);
        }

        var strongTile =
            tile.AverageLuminance >= options.MinimumSingleTileAverageLuminance ||
            tile.Percentile95Luminance >= options.MinimumSingleTilePercentile95Luminance ||
            tile.BrightPixelRatio >= options.MinimumSingleTileBrightPixelRatio ||
            tile.RedDominantPixelRatio >= options.MinimumSingleTileRedPixelRatio;

        if (!strongTile || signalAreaRatio < options.MinimumSingleTileSignalAreaRatio)
        {
            return null;
        }

        return FitSignalRegion(preferredRegion ?? tileRegion, tileRegion, options.MaskFit).Pad(options.Padding);
    }

    private static EffectRegion ComponentTileRegion(FrameTileSummary summary, IReadOnlyList<int> component)
    {
        var region = TileRegion(summary.Tiles[component[0]], summary);
        for (var index = 1; index < component.Count; index++)
        {
            region = region.Union(TileRegion(summary.Tiles[component[index]], summary));
        }

        return region;
    }

    private static EffectRegion? SignalRegionForComponent(
        FrameTileSummary summary,
        IReadOnlyList<int> component,
        RegionalMaskBuildOptions options)
    {
        EffectRegion? region = null;
        foreach (var index in component)
        {
            var tile = summary.Tiles[index];
            var signal = options.PreferredSignalRegion?.Invoke(index, tile) ?? tile.SignalBounds;
            if (signal is null)
            {
                continue;
            }

            region = region is null
                ? signal.Clamp()
                : region.Union(signal);
        }

        return region;
    }

    private static EffectRegion FitSignalRegion(
        EffectRegion signalRegion,
        EffectRegion tileRegion,
        LocalMaskFitPreset maskFit)
    {
        if (maskFit == LocalMaskFitPreset.Expanded)
        {
            return tileRegion;
        }

        var expansion = maskFit == LocalMaskFitPreset.Balanced ? 0.22 : 0;
        var signal = signalRegion.Clamp();
        var tile = tileRegion.Clamp();
        if (expansion <= 0)
        {
            return ConstrainTo(signal, tile);
        }

        var x = signal.X - ((signal.X - tile.X) * expansion);
        var y = signal.Y - ((signal.Y - tile.Y) * expansion);
        var right = signal.Right + ((tile.Right - signal.Right) * expansion);
        var bottom = signal.Bottom + ((tile.Bottom - signal.Bottom) * expansion);

        return ConstrainTo(new EffectRegion(x, y, right - x, bottom - y), tile);
    }

    private static EffectRegion ConstrainTo(EffectRegion region, EffectRegion bounds)
    {
        var candidate = region.Clamp();
        var limit = bounds.Clamp();
        var x = Math.Max(candidate.X, limit.X);
        var y = Math.Max(candidate.Y, limit.Y);
        var right = Math.Min(candidate.Right, limit.Right);
        var bottom = Math.Min(candidate.Bottom, limit.Bottom);

        return new EffectRegion(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y)).Clamp();
    }

    private static IReadOnlyList<EffectRegion> MergeRegions(IReadOnlyList<EffectRegion> regions, double padding)
    {
        var merged = new List<EffectRegion>();
        var tolerance = Math.Max(0, padding);

        foreach (var region in regions)
        {
            var candidate = region.Clamp();
            var mergedIntoExisting = false;
            for (var index = 0; index < merged.Count; index++)
            {
                if (!merged[index].TouchesOrOverlaps(candidate, tolerance))
                {
                    continue;
                }

                merged[index] = merged[index].Union(candidate);
                mergedIntoExisting = true;
                break;
            }

            if (!mergedIntoExisting)
            {
                merged.Add(candidate);
            }
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            for (var index = 0; index < merged.Count; index++)
            {
                for (var otherIndex = index + 1; otherIndex < merged.Count; otherIndex++)
                {
                    if (!merged[index].TouchesOrOverlaps(merged[otherIndex], tolerance))
                    {
                        continue;
                    }

                    merged[index] = merged[index].Union(merged[otherIndex]);
                    merged.RemoveAt(otherIndex);
                    changed = true;
                    break;
                }

                if (changed)
                {
                    break;
                }
            }
        }

        return merged;
    }

    private static double EffectiveMergeTolerance(RegionalMaskBuildOptions options) =>
        options.MergeTolerance > 0 ? options.MergeTolerance : options.Padding;
}

internal sealed record RegionalMaskBuildOptions
{
    public double Padding { get; init; }

    public double MergeTolerance { get; init; }

    public LocalMaskFitPreset MaskFit { get; init; } = LocalMaskFitPreset.Precise;

    public bool IncludeNeighboringBrightTiles { get; init; }

    public double MaximumTileRatio { get; init; } = 0.35;

    public double NeighborBrightAverageThreshold { get; init; } = double.PositiveInfinity;

    public double NeighborBrightPercentile95Threshold { get; init; } = double.PositiveInfinity;

    public double NeighborBrightPixelRatioThreshold { get; init; } = double.PositiveInfinity;

    public double NeighborRedDominantPixelRatioThreshold { get; init; } = double.PositiveInfinity;

    public double MinimumSingleTileAverageLuminance { get; init; } = 0.62;

    public double MinimumSingleTilePercentile95Luminance { get; init; } = 0.72;

    public double MinimumSingleTileBrightPixelRatio { get; init; } = 0.10;

    public double MinimumSingleTileRedPixelRatio { get; init; } = 0.35;

    public double MinimumSingleTileSignalAreaRatio { get; init; } = 0.04;

    public double MinimumComponentSignalAreaRatio { get; init; } = 0.025;

    public bool AllowSingleTileWithoutSignal { get; init; }

    public bool TrustPreferredSignalRegion { get; init; }

    public Func<int, TileLuminanceStats, EffectRegion?>? PreferredSignalRegion { get; init; }
}

internal sealed record RegionalMaskBuildResult(IReadOnlyList<EffectRegion> Regions, int TileCount)
{
    public static RegionalMaskBuildResult Empty { get; } = new(Array.Empty<EffectRegion>(), 0);
}
