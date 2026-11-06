using System.Drawing;
using Dameview.UI.Foundation;

namespace Dameview.UI.Layout;

/// <summary>Lays children out in rows of equal height, starting a new row when another wouldn't fit.</summary>
/// <remarks>Each row shares the whole width between its children, so a short last row leaves no hole.</remarks>
internal sealed class WrapGrid : UiElement
{
    private readonly float _minCellWidth;
    private readonly float _rowHeight;

    internal WrapGrid(float minCellWidth, float rowHeight, params UiElement[] children)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minCellWidth);
        _minCellWidth = minCellWidth;
        _rowHeight = rowHeight;
        foreach (UiElement child in children)
        {
            AddChild(child);
        }
    }

    internal float Spacing { get; init; }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        int count = CountVisibleChildren();
        if (count == 0)
        {
            return SizeF.Empty;
        }

        float width = float.IsFinite(availableSize.Width)
            ? availableSize.Width
            : (count * (_minCellWidth + Spacing)) - Spacing;
        int columns = GetColumns(width, count);
        int cell = 0;
        for (int index = 0; index < Children.Count; index++)
        {
            if (Children[index].IsVisible)
            {
                Children[index].Measure(GetCell(cell++, count, columns, width).Size);
            }
        }

        int rows = (count + columns - 1) / columns;
        return new SizeF(width, (rows * (_rowHeight + Spacing)) - Spacing);
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        int count = CountVisibleChildren();
        int columns = GetColumns(finalSize.Width, count);
        int cell = 0;
        for (int index = 0; index < Children.Count; index++)
        {
            if (Children[index].IsVisible)
            {
                Children[index].Arrange(GetCell(cell++, count, columns, finalSize.Width));
            }
        }
    }

    private int CountVisibleChildren()
    {
        int count = 0;
        for (int index = 0; index < Children.Count; index++)
        {
            if (Children[index].IsVisible)
            {
                count++;
            }
        }

        return count;
    }

    private int GetColumns(float width, int count) =>
        Math.Clamp((int)((width + Spacing) / (_minCellWidth + Spacing)), 1, Math.Max(1, count));

    private RectangleF GetCell(int index, int count, int columns, float width)
    {
        int row = index / columns;
        int cellsInRow = Math.Min(columns, count - (row * columns));
        float cellWidth = MathF.Max(0.0f, (width - (Spacing * (cellsInRow - 1))) / cellsInRow);
        return new RectangleF(
            (index % columns) * (cellWidth + Spacing),
            row * (_rowHeight + Spacing),
            cellWidth,
            _rowHeight);
    }
}
