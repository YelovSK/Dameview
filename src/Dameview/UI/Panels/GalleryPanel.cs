using System.Drawing;
using Dameview.Imaging;
using Dameview.Navigation;
using Dameview.Platform;
using Dameview.UI.Layout;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class GalleryPanel : UiElement, IDisposable
{
    internal const float DefaultWidthDips = 184.0f;
    internal const float ItemHeightDips = 142.0f;

    private const float PanelPadding = 8.0f;
    private const float ItemPadding = 6.0f;
    private const float LabelHeight = 24.0f;
    private const float MinimumItemWidthDips = 132.0f;
    private const float WheelStep = 72.0f;

    private readonly ID2D1DeviceContext _deviceContext;
    private readonly IDWriteTextFormat _labelFormat;
    private readonly IDWriteInlineObject _ellipsisSign;
    private readonly IThumbnailLoader _thumbnailLoader;
    private readonly Action<string> _openImage;
    private readonly Scrollbar _scrollbar;
    private readonly ScrollOffsetController _scrollOffset = new();
    private readonly Dictionary<string, ThumbnailSlot> _slots =
        new(StringComparer.OrdinalIgnoreCase);
    private FolderEntry[] _entries = [];
    private string? _selectedPath;
    private int _hoveredIndex = -1;
    private int _pressedIndex = -1;

    internal GalleryPanel(
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        IThumbnailLoader thumbnailLoader,
        Action<string> openImage)
    {
        _deviceContext = deviceContext;
        _thumbnailLoader = thumbnailLoader;
        _openImage = openImage;
        _scrollbar = new Scrollbar(SetScrollOffset);
        AddChild(_scrollbar);
        _labelFormat = directWriteFactory.CreateTextFormat(
            UiTypography.FontFamily,
            FontWeight.Normal,
            FontStyle.Normal,
            12.0f);
        _labelFormat.TextAlignment = TextAlignment.Center;
        _labelFormat.ParagraphAlignment = ParagraphAlignment.Center;
        _labelFormat.WordWrapping = WordWrapping.NoWrap;
        _ellipsisSign = directWriteFactory.CreateEllipsisTrimmingSign(_labelFormat);
        _labelFormat.SetTrimming(
            new Trimming { Granularity = TrimmingGranularity.Character },
            _ellipsisSign);
    }

    internal override bool PreservesFocusOnPointerPress => true;
    internal override UiCursor Cursor => _hoveredIndex >= 0 ? UiCursor.Pointer : UiCursor.Default;

    internal void ApplyState(FolderEntry[] entries, string? selectedPath)
    {
        bool entriesChanged = !ReferenceEquals(_entries, entries);
        bool selectionChanged = !string.Equals(
            _selectedPath,
            selectedPath,
            StringComparison.OrdinalIgnoreCase);
        if (!entriesChanged && !selectionChanged)
        {
            return;
        }

        if (entriesChanged)
        {
            _entries = entries;
            ClearSlots();
            _hoveredIndex = -1;
            _pressedIndex = -1;
        }

        _selectedPath = selectedPath;
        if (selectionChanged || entriesChanged)
        {
            ScrollSelectionIntoView();
        }

        UpdateScrollMetrics();
        RefreshVisibleThumbnails();
        InvalidateVisual();
    }

    protected override SizeF MeasureCore(SizeF availableSize) => new(
        MathF.Min(DefaultWidthDips, MathF.Max(0.0f, availableSize.Width)),
        MathF.Max(0.0f, availableSize.Height));

    protected override void ArrangeCore(SizeF finalSize)
    {
        ScrollSelectionIntoView();
        UpdateScrollMetrics();
        RefreshVisibleThumbnails();
        _scrollbar.Arrange(new RectangleF(
            MathF.Max(0.0f, finalSize.Width - ScrollbarWidth),
            0.0f,
            ScrollbarWidth,
            finalSize.Height));
        _scrollbar.SetMetrics(ContentHeight, finalSize.Height, _scrollOffset.Offset);
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        if (Bounds.Width <= 0.0f || Bounds.Height <= 0.0f)
        {
            return;
        }

        var panel = new RoundedRectangle(
            new RectangleF(0.0f, 0.0f, Bounds.Width, Bounds.Height),
            UiDesign.PanelCornerRadius,
            UiDesign.PanelCornerRadius);
        context.FillRoundedRectangle(panel, context.Palette.Surface);
        context.DrawRoundedRectangle(panel, context.Palette.SurfaceBorder);

        (int first, int lastExclusive) = GetVisibleRange(
            _entries.Length,
            _scrollOffset.Offset,
            Bounds.Height,
            ItemHeightDips,
            ColumnCount);
        for (int index = first; index < lastExclusive; index++)
        {
            DrawItem(context, index);
        }
    }

    internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
    {
        bool isInside = input.Position.X >= 0.0f
            && input.Position.X < Bounds.Width
            && input.Position.Y >= 0.0f
            && input.Position.Y < Bounds.Height;
        int index = isInside && input.Position.X < ContentWidth
            ? HitTestIndex(
                input.Position.X,
                input.Position.Y,
                _scrollOffset.Offset,
                _entries.Length,
                ItemWidth,
                ItemHeightDips,
                ColumnCount)
            : -1;
        switch (input.Kind)
        {
            case UiPointerEventKind.Moved:
                bool changed = _hoveredIndex != index;
                _hoveredIndex = index;
                return new UiPointerResult(Consumed: true, NeedsRepaint: changed);

            case UiPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                _pressedIndex = index;
                return new UiPointerResult(Consumed: true, NeedsRepaint: true, CapturePointer: index >= 0);

            case UiPointerEventKind.Released:
                int pressed = _pressedIndex;
                _pressedIndex = -1;
                if (pressed >= 0 && pressed == index)
                {
                    _openImage(_entries[pressed].FullName);
                }

                return new UiPointerResult(Consumed: true, NeedsRepaint: pressed >= 0);

            case UiPointerEventKind.Cancelled:
                bool wasPressed = _pressedIndex >= 0;
                _pressedIndex = -1;
                return new UiPointerResult(Consumed: true, NeedsRepaint: wasPressed);

            case UiPointerEventKind.Wheel:
                bool scrollChanged = _scrollOffset.ScrollBy(-input.WheelDelta / 120.0f * WheelStep);

                return new UiPointerResult(Consumed: true, NeedsRepaint: scrollChanged);

            default:
                return new UiPointerResult(Consumed: true);
        }
    }

    public void Dispose()
    {
        ClearSlots();
        _ellipsisSign.Dispose();
        _labelFormat.Dispose();
    }

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        if (!_scrollOffset.Update(context.ElapsedSeconds))
        {
            return false;
        }

        RefreshVisibleThumbnails();
        _scrollbar.SetMetrics(ContentHeight, Bounds.Height, _scrollOffset.Offset);
        InvalidateVisual();
        return true;
    }

    protected override void OnVisualStateChanged()
    {
        if (!HasVisualState(UiVisualState.Hovered))
        {
            _hoveredIndex = -1;
        }
    }

    internal static (int First, int LastExclusive) GetVisibleRange(
        int count,
        float scrollOffset,
        float viewportHeight,
        float itemHeight,
        int columnCount = 1)
    {
        if (count <= 0 || viewportHeight <= 0.0f || itemHeight <= 0.0f)
        {
            return (0, 0);
        }

        columnCount = Math.Max(1, columnCount);
        int firstRow = Math.Max(0, (int)MathF.Floor((scrollOffset - PanelPadding) / itemHeight) - 1);
        int lastRow = Math.Max(
            firstRow,
            (int)MathF.Ceiling((scrollOffset + viewportHeight - PanelPadding) / itemHeight) + 1);
        return (
            Math.Min(count, firstRow * columnCount),
            Math.Min(count, lastRow * columnCount));
    }

    internal static int HitTestIndex(float y, float scrollOffset, int count, float itemHeight)
        => HitTestIndex(PanelPadding, y, scrollOffset, count, float.MaxValue, itemHeight, 1);

    internal static int HitTestIndex(
        float x,
        float y,
        float scrollOffset,
        int count,
        float itemWidth,
        float itemHeight,
        int columnCount,
        float itemGap = 0.0f)
    {
        if (count <= 0 || itemWidth <= 0.0f || itemHeight <= 0.0f || columnCount <= 0)
        {
            return -1;
        }

        float contentX = x - PanelPadding;
        float contentY = y + scrollOffset - PanelPadding;
        if (contentX < 0.0f || contentY < 0.0f)
        {
            return -1;
        }

        float columnPitch = itemWidth + itemGap;
        float rowPitch = itemHeight;
        int column = (int)(contentX / columnPitch);
        int row = (int)(contentY / rowPitch);
        if (column >= columnCount
            || contentX - column * columnPitch > itemWidth
            || contentY - row * rowPitch > itemHeight - itemGap)
        {
            return -1;
        }

        int index = row * columnCount + column;
        return index < count ? index : -1;
    }

    private void DrawItem(in UiDrawContext context, int index)
    {
        FolderEntry entry = _entries[index];
        int columnCount = ColumnCount;
        int row = index / columnCount;
        int column = index % columnCount;
        float itemWidth = ItemWidth;
        float y = PanelPadding + row * ItemHeightDips - _scrollOffset.Offset;
        var itemBounds = new RectangleF(
            PanelPadding + column * (itemWidth + UiDesign.SmallSpacing),
            y,
            itemWidth,
            ItemHeightDips - UiDesign.SmallSpacing);
        bool selected = string.Equals(entry.FullName, _selectedPath, StringComparison.OrdinalIgnoreCase);
        if (selected || index == _hoveredIndex || index == _pressedIndex)
        {
            Color4 color = selected
                ? context.Palette.Accent
                : index == _pressedIndex ? context.Palette.ControlPressed : context.Palette.ControlHover;
            context.FillRoundedRectangle(
                new RoundedRectangle(itemBounds, UiDesign.ControlCornerRadius, UiDesign.ControlCornerRadius),
                color,
                selected ? 0.28f : 1.0f);
        }

        float imageHeight = MathF.Max(0.0f, itemBounds.Height - LabelHeight - 2.0f * ItemPadding);
        var imageBounds = new RectangleF(
            itemBounds.X + ItemPadding,
            itemBounds.Y + ItemPadding,
            MathF.Max(0.0f, itemBounds.Width - 2.0f * ItemPadding),
            imageHeight);
        if (_slots.TryGetValue(entry.FullName, out ThumbnailSlot? slot) && slot.Bitmap is { } bitmap)
        {
            float scale = MathF.Min(
                imageBounds.Width / bitmap.PixelSize.Width,
                imageBounds.Height / bitmap.PixelSize.Height);
            float width = bitmap.PixelSize.Width * scale;
            float height = bitmap.PixelSize.Height * scale;
            var destination = new Rect(
                imageBounds.X + (imageBounds.Width - width) / 2.0f,
                imageBounds.Y + (imageBounds.Height - height) / 2.0f,
                width,
                height);
            context.RenderTarget.DrawBitmap(
                bitmap,
                destination,
                context.Opacity,
                BitmapInterpolationMode.Linear,
                new Rect(0.0f, 0.0f, bitmap.PixelSize.Width, bitmap.PixelSize.Height));
        }
        else
        {
            context.FillRoundedRectangle(
                new RoundedRectangle(imageBounds, UiDesign.ControlCornerRadius, UiDesign.ControlCornerRadius),
                context.Palette.OverlaySurface);
        }

        context.DrawText(
            entry.Name,
            _labelFormat,
            new Rect(
                itemBounds.X + ItemPadding,
                itemBounds.Bottom - LabelHeight,
                MathF.Max(0.0f, itemBounds.Width - 2.0f * ItemPadding),
                LabelHeight),
            context.Palette.PrimaryText,
            DrawTextOptions.Clip);
    }

    private void RefreshVisibleThumbnails()
    {
        (int first, int lastExclusive) = GetVisibleRange(
            _entries.Length,
            _scrollOffset.Offset,
            Bounds.Height,
            ItemHeightDips,
            ColumnCount);
        var visiblePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = first; index < lastExclusive; index++)
        {
            string path = _entries[index].FullName;
            visiblePaths.Add(path);
            if (_slots.ContainsKey(path))
            {
                continue;
            }

            var slot = new ThumbnailSlot();
            _slots.Add(path, slot);
            slot.Request = _thumbnailLoader.Request(
                path,
                ThumbnailPriority.Gallery,
                image => CompleteThumbnail(path, slot, image));
        }

        foreach ((string path, ThumbnailSlot slot) in _slots.ToArray())
        {
            if (!visiblePaths.Contains(path))
            {
                _slots.Remove(path);
                slot.Dispose();
            }
        }
    }

    private void CompleteThumbnail(string path, ThumbnailSlot slot, DecodedImage image)
    {
        if (!_slots.TryGetValue(path, out ThumbnailSlot? current) || !ReferenceEquals(current, slot))
        {
            return;
        }

        slot.Request?.Dispose();
        slot.Request = null;
        slot.Bitmap = D2DBitmapFactory.Create(_deviceContext, image);

        InvalidateVisual();
    }

    private void ScrollSelectionIntoView()
    {
        int selectedIndex = Array.FindIndex(
            _entries,
            entry => string.Equals(entry.FullName, _selectedPath, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0 || Bounds.Height <= 0.0f)
        {
            return;
        }

        int row = selectedIndex / ColumnCount;
        float top = PanelPadding + row * ItemHeightDips;
        float bottom = top + ItemHeightDips;
        float target = _scrollOffset.TargetOffset;
        if (top < target)
        {
            target = top;
        }
        else if (bottom > target + Bounds.Height)
        {
            target = bottom - Bounds.Height;
        }

        _scrollOffset.SetImmediate(target);
    }

    private void UpdateScrollMetrics()
    {
        _scrollOffset.SetMaximum(MathF.Max(0.0f, ContentHeight - Bounds.Height));
        _scrollbar.SetMetrics(ContentHeight, Bounds.Height, _scrollOffset.Offset);
    }

    private const float ScrollbarWidth = 12.0f;
    private const float ScrollbarGap = 2.0f;
    private float ContentWidth => MathF.Max(0.0f, Bounds.Width - ScrollbarWidth - ScrollbarGap);
    private int ColumnCount => GetColumnCount(ContentWidth);
    private float ItemWidth => GetItemWidth(ContentWidth, ColumnCount);
    private float ContentHeight => 2.0f * PanelPadding + RowCount * ItemHeightDips;
    private int RowCount => (_entries.Length + ColumnCount - 1) / ColumnCount;

    private static int GetColumnCount(float contentWidth)
    {
        float pitch = MinimumItemWidthDips + UiDesign.SmallSpacing;
        return Math.Max(1, (int)MathF.Floor((contentWidth + UiDesign.SmallSpacing) / pitch));
    }

    private static float GetItemWidth(float contentWidth, int columnCount)
    {
        return MathF.Max(
            0.0f,
            (contentWidth - (columnCount - 1) * UiDesign.SmallSpacing) / columnCount);
    }

    private void SetScrollOffset(float offset)
    {
        if (!_scrollOffset.SetImmediate(offset))
        {
            return;
        }

        RefreshVisibleThumbnails();
        _scrollbar.SetMetrics(ContentHeight, Bounds.Height, _scrollOffset.Offset);
        InvalidateVisual();
    }

    private void ClearSlots()
    {
        foreach (ThumbnailSlot slot in _slots.Values)
        {
            slot.Dispose();
        }

        _slots.Clear();
    }

    private sealed class ThumbnailSlot : IDisposable
    {
        internal IDisposable? Request { get; set; }
        internal ID2D1Bitmap1? Bitmap { get; set; }

        public void Dispose()
        {
            Request?.Dispose();
            Bitmap?.Dispose();
        }
    }
}
