using System.Drawing;
using Dameview.Settings;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;

namespace Dameview.UI.Panels;

/// <summary>
/// Grid geometry for the gallery, answering in the caller's own coordinates.
/// </summary>
/// <remarks>
/// A horizontal gallery is the vertical one turned on its side, so everything is computed
/// against a transposed size and turned back on the way out. Inside, "length" runs along the
/// scroll axis and "breadth" across it; neither is a width or a height until the final transpose.
/// </remarks>
internal readonly struct GalleryLayout
{
    internal const float ScrollbarThickness = 12.0f;

    private const float PanelPadding = 8.0f;
    private const float ItemPadding = 6.0f;
    private const float ScrollbarGap = 2.0f;
    private const int OverscanRows = 1;

    private readonly SizeF _size;
    private readonly UiOrientation _orientation;
    private readonly int _count;
    private readonly float _itemLength;
    private readonly float _itemBreadth;

    internal GalleryLayout(
        SizeF bounds,
        UiOrientation orientation,
        GalleryThumbnailSize thumbnailSize,
        int count)
    {
        _orientation = orientation;
        _count = Math.Max(0, count);
        _size = orientation == UiOrientation.Vertical
            ? bounds
            : new SizeF(bounds.Height, bounds.Width);
        (float length, float minimumBreadth) = GetItemExtents(thumbnailSize, orientation);
        _itemLength = length;

        float content = MathF.Max(
            0.0f,
            _size.Width - ScrollbarThickness - ScrollbarGap - (2.0f * PanelPadding));
        ColumnCount = Math.Max(
            1,
            (int)MathF.Floor(
                (content + UiDesign.SmallSpacing) / (minimumBreadth + UiDesign.SmallSpacing)));
        _itemBreadth = MathF.Max(
            0.0f,
            (content - ((ColumnCount - 1) * UiDesign.SmallSpacing)) / ColumnCount);
    }

    /// <summary>Items per row, at least one even when the panel is too narrow to fit one.</summary>
    internal int ColumnCount { get; }

    internal int RowCount => (_count + ColumnCount - 1) / ColumnCount;

    /// <summary>The scrollable extent of every row, including the padding at both ends.</summary>
    internal float ContentLength => (2.0f * PanelPadding) + (RowCount * _itemLength);

    /// <summary>The visible extent along the scroll axis.</summary>
    internal float ViewportLength => _size.Height;

    internal float MaximumScrollOffset => MathF.Max(0.0f, ContentLength - ViewportLength);

    /// <summary>An item's size in the caller's coordinates.</summary>
    internal SizeF ItemSize => Transpose(LayoutItemSize);

    /// <summary>The width a label is laid out at, which no amount of scrolling changes.</summary>
    internal RectangleF ScrollbarBounds => Transpose(new RectangleF(
        MathF.Max(0.0f, _size.Width - ScrollbarThickness),
        0.0f,
        ScrollbarThickness,
        _size.Height));

    private SizeF LayoutItemSize =>
        new(_itemBreadth, MathF.Max(0.0f, _itemLength - UiDesign.SmallSpacing));

    internal RectangleF GetItemBounds(int index, float scrollOffset)
    {
        SizeF item = LayoutItemSize;
        return Transpose(new RectangleF(
            PanelPadding + (index % ColumnCount * (item.Width + UiDesign.SmallSpacing)),
            PanelPadding + (index / ColumnCount * _itemLength) - scrollOffset,
            item.Width,
            item.Height));
    }

    /// <summary>The area a thumbnail draws into, inset from its item and clear of the label.</summary>
    internal static RectangleF GetThumbnailBounds(RectangleF item, float labelHeight) => new(
        item.X + ItemPadding,
        item.Y + ItemPadding,
        MathF.Max(0.0f, item.Width - (2.0f * ItemPadding)),
        MathF.Max(0.0f, item.Height - labelHeight - (2.0f * ItemPadding)));

    internal static RectangleF GetLabelBounds(RectangleF item, float labelHeight) => new(
        item.X + ItemPadding,
        item.Bottom - labelHeight,
        MathF.Max(0.0f, item.Width - (2.0f * ItemPadding)),
        labelHeight);

    /// <summary>
    /// The half-open range of items worth holding on to, padded by a row at each end so that
    /// scrolling does not have to decode a thumbnail the instant it becomes visible.
    /// </summary>
    internal (int First, int LastExclusive) GetVisibleRange(float scrollOffset)
    {
        if (_count <= 0 || ViewportLength <= 0.0f || _itemLength <= 0.0f)
        {
            return (0, 0);
        }

        int firstRow = Math.Max(
            0,
            (int)MathF.Floor((scrollOffset - PanelPadding) / _itemLength) - OverscanRows);
        int lastRow = Math.Max(
            firstRow,
            (int)MathF.Ceiling((scrollOffset + ViewportLength - PanelPadding) / _itemLength)
                + OverscanRows);
        return (
            Math.Min(_count, firstRow * ColumnCount),
            Math.Min(_count, lastRow * ColumnCount));
    }

    /// <returns>The item under the point, or -1 on padding, a gap, or the scrollbar.</returns>
    internal int HitTest(PointF point, float scrollOffset)
    {
        PointF layout = Transpose(point);
        if (_count <= 0
            || _itemBreadth <= 0.0f
            || _itemLength <= 0.0f
            || layout.X < 0.0f
            || layout.X >= _size.Width - ScrollbarThickness - ScrollbarGap
            || layout.Y < 0.0f
            || layout.Y >= _size.Height)
        {
            return -1;
        }

        float x = layout.X - PanelPadding;
        float y = layout.Y + scrollOffset - PanelPadding;
        if (x < 0.0f || y < 0.0f)
        {
            return -1;
        }

        float columnPitch = _itemBreadth + UiDesign.SmallSpacing;
        int column = (int)(x / columnPitch);
        int row = (int)(y / _itemLength);
        if (column >= ColumnCount
            || x - (column * columnPitch) > _itemBreadth
            || y - (row * _itemLength) > _itemLength - UiDesign.SmallSpacing)
        {
            return -1;
        }

        int index = (row * ColumnCount) + column;
        return index < _count ? index : -1;
    }

    /// <summary>The offset that puts an item's row in the middle of the viewport, where it fits.</summary>
    internal float GetCenteredOffset(int index)
    {
        float center = PanelPadding + (((index / ColumnCount) + 0.5f) * _itemLength);
        return Math.Clamp(center - (ViewportLength / 2.0f), 0.0f, MaximumScrollOffset);
    }

    /// <summary>The nearest offset that brings an item's row fully into view.</summary>
    internal float GetRevealOffset(int index, float scrollOffset)
    {
        float top = PanelPadding + (index / ColumnCount * _itemLength);
        if (top < scrollOffset)
        {
            return top;
        }

        float bottom = top + _itemLength;
        return bottom > scrollOffset + ViewportLength ? bottom - ViewportLength : scrollOffset;
    }

    // Along the scroll axis an item takes a whole row; across it, only a minimum that the
    // columns then grow from. Turning the gallery on its side swaps which axis gets which.
    private static (float Length, float MinimumBreadth) GetItemExtents(
        GalleryThumbnailSize size,
        UiOrientation orientation)
    {
        (float height, float minimumWidth) = size switch
        {
            GalleryThumbnailSize.Small => (110.0f, 96.0f),
            GalleryThumbnailSize.Medium => (142.0f, 132.0f),
            GalleryThumbnailSize.Large => (190.0f, 176.0f),
            _ => throw new ArgumentOutOfRangeException(nameof(size)),
        };

        return orientation == UiOrientation.Vertical
            ? (height, minimumWidth)
            : (minimumWidth + UiDesign.SmallSpacing, height - UiDesign.SmallSpacing);
    }

    private PointF Transpose(PointF point) => _orientation == UiOrientation.Vertical
        ? point
        : new PointF(point.Y, point.X);

    private SizeF Transpose(SizeF size) => _orientation == UiOrientation.Vertical
        ? size
        : new SizeF(size.Height, size.Width);

    private RectangleF Transpose(RectangleF bounds) => _orientation == UiOrientation.Vertical
        ? bounds
        : new RectangleF(bounds.Y, bounds.X, bounds.Height, bounds.Width);
}
