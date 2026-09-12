using System.Drawing;

namespace Dameview.Viewing;

internal readonly record struct PaneLayoutArea(
    float Width,
    float Height,
    float SplitterSize,
    float PaneHeaderHeight,
    float MinimumPaneSize);

internal static class PaneLayoutOptimizer
{
    private static readonly WorkspaceSplitOrientation[] Orientations =
        Enum.GetValues<WorkspaceSplitOrientation>();
    private static readonly float[] RatioSearchSteps = [0.1f, 0.025f, 0.00625f];
    private const int MaximumExhaustivePaneCount = 10;
    private const int AspectBucketCount = 32;
    private const double MinimumAspect = 0.001;
    private const double MaximumAspect = 1000.0;
    private const float MinimumRatio = 0.01f;

    internal static WorkspaceNode Optimize(
        WorkspaceNode current,
        PaneLayoutArea area,
        Func<ViewerPane, SizeF> getImageSize)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(getImageSize);
        ValidateArea(area);

        ViewerPane[] panes = [.. EnumeratePanes(current)];
        float fallbackSize = MathF.Max(area.Width, area.Height);
        Dictionary<ViewerPane, SizeF> imageSizes = panes.ToDictionary(
            pane => pane,
            pane => NormalizeImageSize(getImageSize(pane), fallbackSize));
        Candidate currentCandidate = OptimizeExisting(current, imageSizes);
        OptimizeRatios(currentCandidate.Root, area, imageSizes);
        if (panes.Length < 2)
        {
            return currentCandidate.Root;
        }

        float targetAspect = area.Width / area.Height;
        if (panes.Length > MaximumExhaustivePaneCount)
        {
            Candidate greedy = OptimizeGreedy(panes, targetAspect, imageSizes);
            OptimizeRatios(greedy.Root, area, imageSizes);
            return GetScore(greedy.Root, area, imageSizes)
                > GetScore(currentCandidate.Root, area, imageSizes) + 0.000001
                    ? greedy.Root
                    : currentCandidate.Root;
        }

        var candidates = new Dictionary<ulong, List<Candidate>>();
        for (int index = 0; index < panes.Length; index++)
        {
            candidates[1UL << index] =
            [
                new Candidate(panes[index], GetAspect(imageSizes[panes[index]])),
            ];
        }

        ulong fullMask = (1UL << panes.Length) - 1UL;
        for (int paneCount = 2; paneCount <= panes.Length; paneCount++)
        {
            for (ulong mask = 1; mask <= fullMask; mask++)
            {
                if ((int)ulong.PopCount(mask) != paneCount)
                {
                    continue;
                }

                var buckets = new Candidate?[AspectBucketCount];
                ulong anchor = mask & (~mask + 1UL);
                for (ulong firstMask = (mask - 1UL) & mask;
                    firstMask != 0;
                    firstMask = (firstMask - 1UL) & mask)
                {
                    if ((firstMask & anchor) == 0)
                    {
                        continue;
                    }

                    ulong secondMask = mask ^ firstMask;
                    if (secondMask == 0)
                    {
                        continue;
                    }

                    foreach (Candidate first in candidates[firstMask])
                    {
                        foreach (Candidate second in candidates[secondMask])
                        {
                            AddCandidate(buckets, Combine(first, second, WorkspaceSplitOrientation.Horizontal));
                            AddCandidate(buckets, Combine(first, second, WorkspaceSplitOrientation.Vertical));
                        }
                    }
                }

                candidates[mask] = [.. buckets.OfType<Candidate>()];
            }
        }

        Candidate best = currentCandidate;
        double bestScore = GetScore(best.Root, area, imageSizes);
        foreach (Candidate candidate in candidates[fullMask])
        {
            WorkspaceNode candidateRoot = CloneLayout(candidate.Root);
            OptimizeRatios(candidateRoot, area, imageSizes);
            double score = GetScore(candidateRoot, area, imageSizes);
            if (score > bestScore + 0.000001)
            {
                best = candidate with { Root = candidateRoot };
                bestScore = score;
            }
        }

        return best.Root;
    }

    private static Candidate OptimizeExisting(
        WorkspaceNode node,
        Dictionary<ViewerPane, SizeF> imageSizes)
    {
        return node switch
        {
            ViewerPane pane => new Candidate(pane, GetAspect(imageSizes[pane])),
            WorkspaceSplit split => Combine(
                OptimizeExisting(split.First, imageSizes),
                OptimizeExisting(split.Second, imageSizes),
                split.Orientation),
            _ => throw new InvalidOperationException($"Unsupported workspace node: {node.GetType().Name}."),
        };
    }

    private static Candidate OptimizeGreedy(
        IReadOnlyList<ViewerPane> panes,
        float targetAspect,
        Dictionary<ViewerPane, SizeF> imageSizes)
    {
        var candidates = panes
            .Select(pane => new Candidate(pane, GetAspect(imageSizes[pane])))
            .ToList();
        while (candidates.Count > 1)
        {
            int bestFirst = 0;
            int bestSecond = 1;
            Candidate best = Combine(
                candidates[bestFirst],
                candidates[bestSecond],
                WorkspaceSplitOrientation.Horizontal);
            double bestWaste = GetAspectWaste(best.Aspect, targetAspect);
            for (int firstIndex = 0; firstIndex < candidates.Count - 1; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < candidates.Count; secondIndex++)
                {
                    foreach (WorkspaceSplitOrientation orientation in Orientations)
                    {
                        Candidate combined = Combine(
                            candidates[firstIndex],
                            candidates[secondIndex],
                            orientation);
                        double waste = GetAspectWaste(combined.Aspect, targetAspect);
                        if (waste >= bestWaste)
                        {
                            continue;
                        }

                        bestFirst = firstIndex;
                        bestSecond = secondIndex;
                        best = combined;
                        bestWaste = waste;
                    }
                }
            }

            candidates.RemoveAt(bestSecond);
            candidates[bestFirst] = best;
        }

        return candidates[0];
    }

    private static Candidate Combine(
        Candidate first,
        Candidate second,
        WorkspaceSplitOrientation orientation)
    {
        double total = first.Aspect + second.Aspect;
        double aspect;
        double ratio;
        if (orientation == WorkspaceSplitOrientation.Horizontal)
        {
            aspect = total;
            ratio = first.Aspect / total;
        }
        else
        {
            aspect = first.Aspect * second.Aspect / total;
            ratio = second.Aspect / total;
        }

        aspect = Math.Clamp(aspect, MinimumAspect, MaximumAspect);
        float splitRatio = (float)Math.Clamp(ratio, MinimumAspect, 1.0 - MinimumAspect);
        return new Candidate(
            new WorkspaceSplit(orientation, first.Root, second.Root, splitRatio),
            aspect);
    }

    private static void OptimizeRatios(
        WorkspaceNode root,
        PaneLayoutArea area,
        Dictionary<ViewerPane, SizeF> imageSizes)
    {
        WorkspaceSplit[] splits = [.. EnumerateSplits(root)];
        foreach (float step in RatioSearchSteps)
        {
            foreach (WorkspaceSplit split in splits)
            {
                float center = split.Ratio;
                float bestRatio = center;
                double bestScore = GetScore(root, area, imageSizes);
                for (int offset = -4; offset <= 4; offset++)
                {
                    float ratio = Math.Clamp(
                        center + offset * step,
                        MinimumRatio,
                        1.0f - MinimumRatio);
                    split.SetRatio(ratio);
                    double score = GetScore(root, area, imageSizes);
                    if (score > bestScore + 0.000001)
                    {
                        bestRatio = ratio;
                        bestScore = score;
                    }
                }

                split.SetRatio(bestRatio);
            }
        }
    }

    private static double GetScore(
        WorkspaceNode node,
        PaneLayoutArea area,
        Dictionary<ViewerPane, SizeF> imageSizes)
    {
        if (node is ViewerPane pane)
        {
            SizeF image = imageSizes[pane];
            float contentHeight = MathF.Max(0.0f, area.Height - area.PaneHeaderHeight);
            float scale = MathF.Min(
                1.0f,
                MathF.Min(area.Width / image.Width, contentHeight / image.Height));
            double displayedArea = Math.Max(0.000001, image.Width * image.Height * scale * scale);
            return Math.Log(displayedArea);
        }

        if (node is not WorkspaceSplit split)
        {
            throw new InvalidOperationException($"Unsupported workspace node: {node.GetType().Name}.");
        }

        bool horizontal = split.Orientation == WorkspaceSplitOrientation.Horizontal;
        float mainLength = horizontal ? area.Width : area.Height;
        float splitterSize = MathF.Min(area.SplitterSize, MathF.Max(0.0f, mainLength));
        float usableLength = MathF.Max(0.0f, mainLength - splitterSize);
        float firstLength = usableLength * split.Ratio;
        if (usableLength < 2.0f * area.MinimumPaneSize)
        {
            firstLength = Math.Clamp(firstLength, 0.0f, usableLength);
        }
        else
        {
            firstLength = Math.Clamp(
                firstLength,
                area.MinimumPaneSize,
                usableLength - area.MinimumPaneSize);
        }

        float secondLength = usableLength - firstLength;
        PaneLayoutArea firstArea = horizontal
            ? area with { Width = firstLength }
            : area with { Height = firstLength };
        PaneLayoutArea secondArea = horizontal
            ? area with { Width = secondLength }
            : area with { Height = secondLength };
        return GetScore(split.First, firstArea, imageSizes)
            + GetScore(split.Second, secondArea, imageSizes);
    }

    private static void AddCandidate(Candidate?[] buckets, Candidate candidate)
    {
        // Keep representative shapes across the aspect range instead of retaining an
        // exponentially growing set of equivalent slicing trees.
        double logRange = Math.Log(MaximumAspect / MinimumAspect);
        double position = Math.Log(candidate.Aspect / MinimumAspect) / logRange;
        int bucket = Math.Clamp((int)(position * AspectBucketCount), 0, AspectBucketCount - 1);
        double bucketCenter = Math.Exp(
            Math.Log(MinimumAspect)
            + (bucket + 0.5) * logRange / AspectBucketCount);
        if (buckets[bucket] is not { } existing
            || Math.Abs(Math.Log(candidate.Aspect / bucketCenter))
                < Math.Abs(Math.Log(existing.Aspect / bucketCenter)))
        {
            buckets[bucket] = candidate;
        }
    }

    private static double GetAspectWaste(double layoutAspect, double targetAspect)
    {
        double ratio = layoutAspect / targetAspect;
        return 1.0 - Math.Min(ratio, 1.0 / ratio);
    }

    private static double GetAspect(SizeF size) =>
        Math.Clamp(size.Width / size.Height, MinimumAspect, MaximumAspect);

    private static SizeF NormalizeImageSize(SizeF size, float fallbackSize)
    {
        return size.Width > 0.0f
            && size.Height > 0.0f
            && float.IsFinite(size.Width)
            && float.IsFinite(size.Height)
                ? size
                : new SizeF(fallbackSize, fallbackSize);
    }

    private static void ValidateArea(PaneLayoutArea area)
    {
        if (!(area.Width > 0.0f)
            || !(area.Height > 0.0f)
            || !float.IsFinite(area.Width)
            || !float.IsFinite(area.Height)
            || area.SplitterSize < 0.0f
            || area.PaneHeaderHeight < 0.0f
            || area.MinimumPaneSize < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(area));
        }
    }

    private static IEnumerable<ViewerPane> EnumeratePanes(WorkspaceNode node)
    {
        if (node is ViewerPane pane)
        {
            yield return pane;
            yield break;
        }

        if (node is not WorkspaceSplit split)
        {
            throw new InvalidOperationException($"Unsupported workspace node: {node.GetType().Name}.");
        }

        foreach (ViewerPane child in EnumeratePanes(split.First))
        {
            yield return child;
        }

        foreach (ViewerPane child in EnumeratePanes(split.Second))
        {
            yield return child;
        }
    }

    private static WorkspaceNode CloneLayout(WorkspaceNode node) => node switch
    {
        ViewerPane pane => pane,
        WorkspaceSplit split => new WorkspaceSplit(
            split.Orientation,
            CloneLayout(split.First),
            CloneLayout(split.Second),
            split.Ratio),
        _ => throw new InvalidOperationException($"Unsupported workspace node: {node.GetType().Name}."),
    };

    private static IEnumerable<WorkspaceSplit> EnumerateSplits(WorkspaceNode node)
    {
        if (node is not WorkspaceSplit split)
        {
            yield break;
        }

        yield return split;
        foreach (WorkspaceSplit child in EnumerateSplits(split.First))
        {
            yield return child;
        }

        foreach (WorkspaceSplit child in EnumerateSplits(split.Second))
        {
            yield return child;
        }
    }

    private sealed record Candidate(WorkspaceNode Root, double Aspect);
}
