using System.Drawing;
using System.Runtime.InteropServices;

namespace Dameview.UI.Foundation;

/// <summary>The part of a pixel surface that has to be drawn again, kept as a few rectangles.</summary>
/// <remarks>
/// Each rectangle costs a walk of the tree, so overlapping ones are merged, and once there are
/// too many the pair that grows least when joined is merged as well.
/// </remarks>
internal sealed class UiDirtyRegion
{
    internal const int MaxRectangles = 8;

    private readonly List<Rectangle> _rectangles = [];
    private Rectangle _surface;

    internal ReadOnlySpan<Rectangle> Rectangles => CollectionsMarshal.AsSpan(_rectangles);
    internal bool IsEmpty => _rectangles.Count == 0;
    internal bool IsFull => _rectangles.Count == 1 && _rectangles[0] == _surface;

    /// <summary>Empties the region and sets the surface it lies on.</summary>
    internal void Reset(Size surfaceSize)
    {
        _surface = new Rectangle(Point.Empty, surfaceSize);
        _rectangles.Clear();
    }

    internal void AddFull()
    {
        _rectangles.Clear();
        if (!_surface.IsEmpty)
        {
            _rectangles.Add(_surface);
        }
    }

    internal void Add(UiDirtyRegion other)
    {
        foreach (Rectangle rectangle in other._rectangles)
        {
            Add(rectangle);
        }
    }

    internal void Add(Rectangle rectangle)
    {
        // Rectangle.IsEmpty only means all zeros, and a clipped one can keep its place with no area.
        rectangle.Intersect(_surface);
        if (rectangle.Width <= 0 || rectangle.Height <= 0 || IsFull)
        {
            return;
        }

        // Joining two rectangles can make the result overlap others, so keep going until it doesn't.
        bool merged;
        do
        {
            merged = false;
            for (int index = _rectangles.Count - 1; index >= 0; index--)
            {
                if (_rectangles[index].IntersectsWith(rectangle))
                {
                    rectangle = Rectangle.Union(rectangle, _rectangles[index]);
                    _rectangles.RemoveAt(index);
                    merged = true;
                }
            }
        }
        while (merged);

        _rectangles.Add(rectangle);
        if (_rectangles.Count > MaxRectangles)
        {
            MergeCheapestPair();
        }
    }

    private void MergeCheapestPair()
    {
        int first = 0;
        int second = 1;
        long cheapest = long.MaxValue;
        for (int i = 0; i < _rectangles.Count; i++)
        {
            for (int j = i + 1; j < _rectangles.Count; j++)
            {
                long growth = Area(Rectangle.Union(_rectangles[i], _rectangles[j]))
                    - Area(_rectangles[i]) - Area(_rectangles[j]);
                if (growth < cheapest)
                {
                    cheapest = growth;
                    first = i;
                    second = j;
                }
            }
        }

        var union = Rectangle.Union(_rectangles[first], _rectangles[second]);
        _rectangles.RemoveAt(second);
        _rectangles.RemoveAt(first);
        Add(union);
    }

    private static long Area(Rectangle rectangle) => (long)rectangle.Width * rectangle.Height;
}
