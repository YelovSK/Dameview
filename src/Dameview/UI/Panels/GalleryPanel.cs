using System.Drawing;
using System.Numerics;
using Dameview.Imaging;
using Dameview.Navigation;
using Dameview.Platform;
using Dameview.Settings;
using Dameview.UI.Layout;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class GalleryPanel : UiElement, IDisposable
{
    internal const float DefaultWidthDips = UiDesign.DefaultGalleryWidth;
    internal const float ItemHeightDips = 142.0f;

    private const float PanelPadding = 8.0f;
    private const float ItemPadding = 6.0f;
    private const float LabelHeight = 24.0f;
    private const float MinimumItemWidthDips = 132.0f;
    private const float DragThresholdDips = 4.0f;

    private readonly ID2D1DeviceContext _deviceContext;
    private readonly IDWriteFactory _directWriteFactory;
    private readonly IDWriteTextFormat _labelFormat;
    private readonly IDWriteInlineObject _ellipsisSign;
    private readonly IThumbnailLoader _thumbnailLoader;
    private readonly Action<string> _openImage;
    private readonly Action<string> _openInNewTab;
    private readonly Action<string, WorkspaceDragEvent>? _dragPointer;
    private readonly Scrollbar _scrollbar;
    private readonly Dictionary<string, GalleryItemSlot> _slots =
        new(StringComparer.OrdinalIgnoreCase);
    private GalleryPanelState _state = new();
    private GalleryThumbnailSize _thumbnailSize = GalleryThumbnailSize.Medium;
    private SelectionScrollAlignment? _pendingSelectionScroll;
    private int _hoveredIndex = -1;
    private int _pressedIndex = -1;
    private string? _pressedPath;
    private PointF _pressPosition;
    private bool _dragging;

    internal GalleryPanel(
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        IThumbnailLoader thumbnailLoader,
        Action<string> openImage,
        Action<string> openInNewTab,
        Action<string, WorkspaceDragEvent>? dragPointer = null)
    {
        _deviceContext = deviceContext;
        _directWriteFactory = directWriteFactory;
        _thumbnailLoader = thumbnailLoader;
        _openImage = openImage;
        _openInNewTab = openInNewTab;
        _dragPointer = dragPointer;
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

    internal void Bind(GalleryPanelState state)
    {
        if (ReferenceEquals(_state, state))
        {
            return;
        }

        _state = state;
        _pendingSelectionScroll = null;
        ClearSlots();
        _hoveredIndex = -1;
        _pressedIndex = -1;
        UpdateScrollMetrics();
        RefreshVisibleThumbnails();
        InvalidateVisual();
    }

    internal void ApplyState(FolderEntry[] entries, string? selectedPath)
    {
        bool entriesChanged = !ReferenceEquals(_state.Entries, entries);
        bool selectionChanged = !string.Equals(
            _state.SelectedPath,
            selectedPath,
            StringComparison.OrdinalIgnoreCase);
        if (!entriesChanged && !selectionChanged)
        {
            return;
        }

        if (entriesChanged)
        {
            _state.Entries = entries;
            ClearSlots();
            _hoveredIndex = -1;
            _pressedIndex = -1;
        }

        _state.SelectedPath = selectedPath;
        if (entriesChanged)
        {
            _pendingSelectionScroll = SelectionScrollAlignment.EnsureVisible;
        }

        UpdateScrollMetrics();
        RevealSelectionIfPending();
        RefreshVisibleThumbnails();
        InvalidateVisual();
    }

    internal void CenterSelection()
    {
        _pendingSelectionScroll = SelectionScrollAlignment.Center;
        RevealSelectionIfPending();
    }

    internal void SetThumbnailSize(GalleryThumbnailSize size)
    {
        if (_thumbnailSize == size)
        {
            return;
        }

        _thumbnailSize = size;
        _pendingSelectionScroll = SelectionScrollAlignment.EnsureVisible;
        InvalidateLayout();
    }

    protected override SizeF MeasureCore(SizeF availableSize) => new(
        MathF.Min(DefaultWidthDips, MathF.Max(0.0f, availableSize.Width)),
        MathF.Max(0.0f, availableSize.Height));

    protected override void ArrangeCore(SizeF finalSize)
    {
        UpdateScrollMetrics();
        RevealSelectionIfPending();
        RefreshVisibleThumbnails();
        _scrollbar.Arrange(new RectangleF(
            MathF.Max(0.0f, finalSize.Width - ScrollbarWidth),
            0.0f,
            ScrollbarWidth,
            finalSize.Height));
        _scrollbar.SetMetrics(ContentHeight, finalSize.Height, _state.ScrollOffset.Offset);
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
            _state.Entries.Length,
            _state.ScrollOffset.Offset,
            Bounds.Height,
            ItemHeight,
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
                _state.ScrollOffset.Offset,
                _state.Entries.Length,
                ItemWidth,
                ItemHeight,
                ColumnCount)
            : -1;
        switch (input.Kind)
        {
            case UiPointerEventKind.Moved:
                if (_pressedPath is not null)
                {
                    if (!_dragging && HasCrossedDragThreshold(input.Position))
                    {
                        _dragging = true;
                        _dragPointer?.Invoke(
                            _pressedPath,
                            new WorkspaceDragEvent(WorkspaceDragEventKind.Started, input.Position));
                    }

                    if (_dragging)
                    {
                        _dragPointer?.Invoke(
                            _pressedPath,
                            new WorkspaceDragEvent(WorkspaceDragEventKind.Moved, input.Position));
                    }

                    return new UiPointerResult(Consumed: true, NeedsRepaint: _dragging);
                }

                bool changed = _hoveredIndex != index;
                _hoveredIndex = index;
                return new UiPointerResult(Consumed: true, NeedsRepaint: changed);

            case UiPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                _pressedIndex = index;
                _pressedPath = index >= 0 ? _state.Entries[index].FullName : null;
                _pressPosition = input.Position;
                _dragging = false;
                return new UiPointerResult(Consumed: true, NeedsRepaint: true, CapturePointer: index >= 0);

            case UiPointerEventKind.Pressed when input.Button == PointerButton.Middle:
                if (index >= 0)
                {
                    _openInNewTab(_state.Entries[index].FullName);
                }

                return new UiPointerResult(Consumed: true);

            case UiPointerEventKind.Released:
                int pressed = _pressedIndex;
                string? pressedPath = _pressedPath;
                bool wasDragging = _dragging;
                _pressedIndex = -1;
                _pressedPath = null;
                _dragging = false;
                if (wasDragging && pressedPath is not null)
                {
                    _dragPointer?.Invoke(
                        pressedPath,
                        new WorkspaceDragEvent(WorkspaceDragEventKind.Completed, input.Position));
                }
                else if (pressed >= 0 && pressed == index)
                {
                    _openImage(_state.Entries[pressed].FullName);
                }

                return new UiPointerResult(Consumed: true, NeedsRepaint: pressed >= 0);

            case UiPointerEventKind.Cancelled:
                bool wasPressed = _pressedIndex >= 0;
                if (_dragging && _pressedPath is { } cancelledPath)
                {
                    _dragPointer?.Invoke(
                        cancelledPath,
                        new WorkspaceDragEvent(WorkspaceDragEventKind.Cancelled, input.Position));
                }

                _pressedIndex = -1;
                _pressedPath = null;
                _dragging = false;
                return new UiPointerResult(Consumed: true, NeedsRepaint: wasPressed);

            case UiPointerEventKind.Wheel:
                bool scrollChanged = _state.ScrollOffset.ScrollBy(-input.WheelDelta / 120.0f * ItemHeight / 2.0f);

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
        if (!_state.ScrollOffset.Update(context))
        {
            return false;
        }

        RefreshVisibleThumbnails();
        _scrollbar.SetMetrics(ContentHeight, Bounds.Height, _state.ScrollOffset.Offset);
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

    internal static float GetCenteredSelectionOffset(
        int selectedIndex,
        int itemCount,
        int columnCount,
        float viewportHeight,
        float itemHeight = ItemHeightDips)
    {
        int row = selectedIndex / columnCount;
        int rowCount = (itemCount + columnCount - 1) / columnCount;
        float contentHeight = 2.0f * PanelPadding + rowCount * itemHeight;
        float itemCenter = PanelPadding + (row + 0.5f) * itemHeight;
        return Math.Clamp(
            itemCenter - viewportHeight / 2.0f,
            0.0f,
            MathF.Max(0.0f, contentHeight - viewportHeight));
    }

    private void DrawItem(in UiDrawContext context, int index)
    {
        FolderEntry entry = _state.Entries[index];
        int columnCount = ColumnCount;
        int row = index / columnCount;
        int column = index % columnCount;
        float itemWidth = ItemWidth;
        float y = PanelPadding + row * ItemHeight - _state.ScrollOffset.Offset;
        var itemBounds = new RectangleF(
            PanelPadding + column * (itemWidth + UiDesign.SmallSpacing),
            y,
            itemWidth,
            ItemHeight - UiDesign.SmallSpacing);
        bool selected = string.Equals(entry.FullName, _state.SelectedPath, StringComparison.OrdinalIgnoreCase);
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
        _slots.TryGetValue(entry.FullName, out GalleryItemSlot? slot);
        if (slot?.Bitmap is { } bitmap)
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

        if (slot is not null)
        {
            context.DrawTextLayout(
                slot.LabelLayout,
                new Vector2(itemBounds.X + ItemPadding, itemBounds.Bottom - LabelHeight),
                context.Palette.PrimaryText,
                DrawTextOptions.Clip);
        }
    }

    private void RefreshVisibleThumbnails()
    {
        (int first, int lastExclusive) = GetVisibleRange(
            _state.Entries.Length,
            _state.ScrollOffset.Offset,
            Bounds.Height,
            ItemHeight,
            ColumnCount);
        var visiblePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        float labelWidth = MathF.Max(0.0f, ItemWidth - 2.0f * ItemPadding);
        for (int index = first; index < lastExclusive; index++)
        {
            FolderEntry entry = _state.Entries[index];
            string path = entry.FullName;
            visiblePaths.Add(path);
            if (_slots.TryGetValue(path, out GalleryItemSlot? existing))
            {
                existing.SetLabelLayout(_directWriteFactory, _labelFormat, entry.Name, labelWidth);
                continue;
            }

            var slot = new GalleryItemSlot(_directWriteFactory, _labelFormat, entry.Name, labelWidth);
            _slots.Add(path, slot);
            slot.Request = _thumbnailLoader.Request(
                path,
                ThumbnailPriority.Gallery,
                image => CompleteThumbnail(path, slot, image));
        }

        foreach ((string path, GalleryItemSlot slot) in _slots.ToArray())
        {
            if (!visiblePaths.Contains(path))
            {
                _slots.Remove(path);
                slot.Dispose();
            }
        }
    }

    private void CompleteThumbnail(string path, GalleryItemSlot slot, DecodedImage image)
    {
        if (!_slots.TryGetValue(path, out GalleryItemSlot? current) || !ReferenceEquals(current, slot))
        {
            return;
        }

        slot.Request?.Dispose();
        slot.Request = null;
        slot.Bitmap = D2DBitmapFactory.Create(_deviceContext, image);

        InvalidateVisual();
    }

    private void ScrollSelection(SelectionScrollAlignment alignment)
    {
        int selectedIndex = Array.FindIndex(
            _state.Entries,
            entry => string.Equals(entry.FullName, _state.SelectedPath, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0 || Bounds.Height <= 0.0f)
        {
            return;
        }

        if (alignment == SelectionScrollAlignment.Center)
        {
            if (_state.ScrollOffset.SetTarget(GetCenteredSelectionOffset(
                selectedIndex,
                _state.Entries.Length,
                ColumnCount,
                Bounds.Height,
                ItemHeight)))
            {
                InvalidateVisual();
            }

            return;
        }

        int row = selectedIndex / ColumnCount;
        float top = PanelPadding + row * ItemHeight;
        float bottom = top + ItemHeight;
        float target = _state.ScrollOffset.TargetOffset;
        if (top < target)
        {
            target = top;
        }
        else if (bottom > target + Bounds.Height)
        {
            target = bottom - Bounds.Height;
        }

        _state.ScrollOffset.SetImmediate(target);
    }

    private void RevealSelectionIfPending()
    {
        if (_pendingSelectionScroll is not { } alignment || Bounds.Height <= 0.0f)
        {
            return;
        }

        ScrollSelection(alignment);
        _pendingSelectionScroll = null;
    }

    private void UpdateScrollMetrics()
    {
        _state.ScrollOffset.SetMaximum(MathF.Max(0.0f, ContentHeight - Bounds.Height));
        _scrollbar.SetMetrics(ContentHeight, Bounds.Height, _state.ScrollOffset.Offset);
    }

    private const float ScrollbarWidth = 12.0f;
    private const float ScrollbarGap = 2.0f;
    private float ContentWidth => MathF.Max(0.0f, Bounds.Width - ScrollbarWidth - ScrollbarGap);
    private int ColumnCount => GetColumnCount(ContentWidth);
    private float ItemWidth => GetItemWidth(ContentWidth, ColumnCount);
    private float ItemHeight => _thumbnailSize switch
    {
        GalleryThumbnailSize.Small => 110.0f,
        GalleryThumbnailSize.Medium => ItemHeightDips,
        GalleryThumbnailSize.Large => 190.0f,
        _ => throw new InvalidOperationException("Unknown gallery thumbnail size."),
    };
    private float MinimumItemWidth => _thumbnailSize switch
    {
        GalleryThumbnailSize.Small => 96.0f,
        GalleryThumbnailSize.Medium => MinimumItemWidthDips,
        GalleryThumbnailSize.Large => 176.0f,
        _ => throw new InvalidOperationException("Unknown gallery thumbnail size."),
    };
    private float ContentHeight => 2.0f * PanelPadding + RowCount * ItemHeight;
    private int RowCount => (_state.Entries.Length + ColumnCount - 1) / ColumnCount;

    private int GetColumnCount(float contentWidth)
    {
        float pitch = MinimumItemWidth + UiDesign.SmallSpacing;
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
        if (!_state.ScrollOffset.SetImmediate(offset))
        {
            return;
        }

        RefreshVisibleThumbnails();
        _scrollbar.SetMetrics(ContentHeight, Bounds.Height, _state.ScrollOffset.Offset);
        InvalidateVisual();
    }

    private bool HasCrossedDragThreshold(PointF position)
    {
        return MathF.Abs(position.X - _pressPosition.X) >= DragThresholdDips
            || MathF.Abs(position.Y - _pressPosition.Y) >= DragThresholdDips;
    }

    private void ClearSlots()
    {
        foreach (GalleryItemSlot slot in _slots.Values)
        {
            slot.Dispose();
        }

        _slots.Clear();
    }

    private enum SelectionScrollAlignment
    {
        EnsureVisible,
        Center,
    }

    private sealed class GalleryItemSlot : IDisposable
    {
        private float _labelWidth;

        internal GalleryItemSlot(
            IDWriteFactory directWriteFactory,
            IDWriteTextFormat labelFormat,
            string label,
            float labelWidth)
        {
            LabelLayout = directWriteFactory.CreateTextLayout(label, labelFormat, labelWidth, LabelHeight);
            _labelWidth = labelWidth;
        }

        internal IDisposable? Request { get; set; }
        internal ID2D1Bitmap1? Bitmap { get; set; }
        internal IDWriteTextLayout LabelLayout { get; private set; }

        internal void SetLabelLayout(
            IDWriteFactory directWriteFactory,
            IDWriteTextFormat labelFormat,
            string label,
            float labelWidth)
        {
            if (_labelWidth == labelWidth)
            {
                return;
            }

            LabelLayout.Dispose();
            LabelLayout = directWriteFactory.CreateTextLayout(label, labelFormat, labelWidth, LabelHeight);
            _labelWidth = labelWidth;
        }

        public void Dispose()
        {
            Request?.Dispose();
            Bitmap?.Dispose();
            LabelLayout.Dispose();
        }
    }
}
