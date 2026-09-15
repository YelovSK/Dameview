using System.Drawing;
using System.Numerics;
using Dameview.Imaging.Loading;
using Dameview.Navigation;
using Dameview.Rendering;
using Dameview.Settings;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.UI.Presentation;
using Dameview.UI.Workspace;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class GalleryPanel : UiElement, IDisposable
{
    internal const float DefaultSizeDips = AppSettings.DefaultGallerySizeDips;
    internal const float ItemHeightDips = 142.0f;

    private const float PanelPadding = 8.0f;
    private const float ItemPadding = 6.0f;
    private const float LabelHeight = 24.0f;
    private const float MinimumItemWidthDips = 132.0f;
    private const float DragThresholdDips = 4.0f;
    private const float ThumbnailSharpness = 1.0f;

    private readonly ID2D1DeviceContext _deviceContext;
    private readonly ID2D1DeviceContext _thumbnailScaleContext;
    private readonly IDWriteFactory _directWriteFactory;
    private readonly IDWriteTextFormat _labelFormat;
    private readonly IDWriteInlineObject _ellipsisSign;
    private readonly IThumbnailImageLoader _thumbnailLoader;
    private readonly Action<string> _openImage;
    private readonly Action<string> _openInNewTab;
    private readonly Action<string, WorkspaceDragEvent>? _dragPointer;
    private readonly Scrollbar _scrollbar;
    private readonly Dictionary<string, GalleryItemSlot> _slots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _visiblePaths =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _stalePaths = [];
    private GalleryPanelState _state = new();
    private GalleryThumbnailSize _thumbnailSize = GalleryThumbnailSize.Medium;
    private UiOrientation _orientation = UiOrientation.Vertical;
    private SelectionScrollAlignment? _pendingSelectionScroll;
    private int _hoveredIndex = -1;
    private int _pressedIndex = -1;
    private string? _pressedPath;
    private PointF _pressPosition;
    private bool _dragging;
    private bool _liveResize;

    internal GalleryPanel(
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        IThumbnailImageLoader thumbnailLoader,
        Action<string> openImage,
        Action<string> openInNewTab,
        Action<string, WorkspaceDragEvent>? dragPointer = null)
    {
        _deviceContext = deviceContext;
        using ID2D1Device device = deviceContext.Device;
        _thumbnailScaleContext = device.CreateDeviceContext();
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
    internal override WindowCursor Cursor => _hoveredIndex >= 0 ? WindowCursor.Pointer : WindowCursor.Default;

    internal void Bind(GalleryPanelState state)
    {
        if (ReferenceEquals(_state, state))
        {
            return;
        }

        _state = state;
        _pendingSelectionScroll = null;
        ApplyOrientationToState();
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
            RefreshVisibleThumbnails();
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

    /// <summary>
    /// Enters interactive-resize mode. While active, thumbnails are stretched
    /// from their cached source bitmaps and labels are kept at their current
    /// width so that each pointer move does not rebuild GPU bitmaps or text layouts.
    /// </summary>
    internal void BeginLiveResize() => _liveResize = true;

    /// <summary>Leaves interactive-resize mode and rebuilds thumbnails and labels at the final size.</summary>
    internal void EndLiveResize()
    {
        if (!_liveResize)
        {
            return;
        }

        _liveResize = false;
        RefreshVisibleThumbnails();
        InvalidateVisual();
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

    internal void SetOrientation(UiOrientation orientation)
    {
        if (_orientation == orientation)
        {
            return;
        }

        _orientation = orientation;
        _scrollbar.SetOrientation(orientation);
        ApplyOrientationToState();
        _hoveredIndex = -1;
        _pressedIndex = -1;
        InvalidateLayout();
    }

    protected override SizeF MeasureCore(SizeF availableSize) => new(
        MathF.Min(DefaultSizeDips, MathF.Max(0.0f, availableSize.Width)),
        MathF.Max(0.0f, availableSize.Height));

    protected override void ArrangeCore(SizeF finalSize)
    {
        UpdateScrollMetrics();
        RevealSelectionIfPending();
        RefreshVisibleThumbnails();
        _scrollbar.Arrange(FromLayoutBounds(new RectangleF(
            MathF.Max(0.0f, LayoutSize.Width - ScrollbarThickness),
            0.0f,
            ScrollbarThickness,
            LayoutSize.Height), _orientation));
        SetScrollbarMetrics();
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
            LayoutSize.Height,
            LayoutItemHeight,
            ColumnCount);
        for (int index = first; index < lastExclusive; index++)
        {
            DrawItem(context, index);
        }
    }

    internal override UiPointerResult OnPointerEvent(in WindowPointerEvent input)
    {
        PointF layoutPoint = ToLayoutPoint(input.Position, _orientation);
        bool isInsideContent = layoutPoint.X >= 0.0f
            && layoutPoint.X < LayoutSize.Width - ScrollbarThickness - ScrollbarGap
            && layoutPoint.Y >= 0.0f
            && layoutPoint.Y < LayoutSize.Height;
        int index = isInsideContent
            ? HitTestIndex(
                layoutPoint.X,
                layoutPoint.Y,
                _state.ScrollOffset.Offset,
                _state.Entries.Length,
                ItemWidth,
                LayoutItemHeight,
                ColumnCount,
                UiDesign.SmallSpacing)
            : -1;
        switch (input.Kind)
        {
            case WindowPointerEventKind.Moved:
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

            case WindowPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                _pressedIndex = index;
                _pressedPath = index >= 0 ? _state.Entries[index].FullName : null;
                _pressPosition = input.Position;
                _dragging = false;
                return new UiPointerResult(Consumed: true, NeedsRepaint: true, CapturePointer: index >= 0);

            case WindowPointerEventKind.Pressed when input.Button == PointerButton.Middle:
                if (index >= 0)
                {
                    _openInNewTab(_state.Entries[index].FullName);
                }

                return new UiPointerResult(Consumed: true);

            case WindowPointerEventKind.Released:
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

            case WindowPointerEventKind.Cancelled:
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

            case WindowPointerEventKind.Wheel:
                bool scrollChanged = _state.ScrollOffset.ScrollBy(
                    -input.WheelDelta / 120.0f * LayoutItemHeight / 2.0f);

                return new UiPointerResult(Consumed: true, NeedsRepaint: scrollChanged);

            default:
                return new UiPointerResult(Consumed: true);
        }
    }

    public void Dispose()
    {
        ClearSlots();
        _thumbnailScaleContext.Dispose();
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
        SetScrollbarMetrics();
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
        int row = index / ColumnCount;
        int column = index % ColumnCount;
        float itemWidth = ItemWidth;
        RectangleF itemBounds = FromLayoutBounds(new RectangleF(
            PanelPadding + column * (itemWidth + UiDesign.SmallSpacing),
            PanelPadding + row * LayoutItemHeight - _state.ScrollOffset.Offset,
            itemWidth,
            LayoutItemHeight - UiDesign.SmallSpacing), _orientation);
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
        if (slot?.SourceBitmap is { } sourceBitmap)
        {
            ID2D1Bitmap1 bitmap;
            float width;
            float height;
            if (_liveResize)
            {
                bitmap = sourceBitmap;
                float liveScale = MathF.Min(
                    imageBounds.Width / sourceBitmap.Size.Width,
                    imageBounds.Height / sourceBitmap.Size.Height);
                width = sourceBitmap.Size.Width * liveScale;
                height = sourceBitmap.Size.Height * liveScale;
            }
            else
            {
                float scale = MathF.Min(
                    imageBounds.Width / sourceBitmap.PixelSize.Width,
                    imageBounds.Height / sourceBitmap.PixelSize.Height);
                bitmap = slot.GetDisplayBitmap(
                    _thumbnailScaleContext,
                    sourceBitmap.PixelSize.Width * scale,
                    sourceBitmap.PixelSize.Height * scale,
                    context.Dpi);
                width = bitmap.Size.Width;
                height = bitmap.Size.Height;
            }

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
                new Rect(0.0f, 0.0f, bitmap.Size.Width, bitmap.Size.Height));
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
        if (!IsVisible)
        {
            _visiblePaths.Clear();
            _stalePaths.Clear();
            ClearSlots();
            return;
        }

        (int first, int lastExclusive) = GetVisibleRange(
            _state.Entries.Length,
            _state.ScrollOffset.Offset,
            LayoutSize.Height,
            LayoutItemHeight,
            ColumnCount);
        _visiblePaths.Clear();
        float labelWidth = MathF.Max(0.0f, PhysicalItemWidth - 2.0f * ItemPadding);
        for (int index = first; index < lastExclusive; index++)
        {
            FolderEntry entry = _state.Entries[index];
            string path = entry.FullName;
            _visiblePaths.Add(path);
            if (_slots.TryGetValue(path, out GalleryItemSlot? existing))
            {
                if (!_liveResize)
                {
                    existing.SetLabelLayout(_directWriteFactory, _labelFormat, entry.Name, labelWidth);
                }

                continue;
            }

            var slot = new GalleryItemSlot(_directWriteFactory, _labelFormat, entry.Name, labelWidth);
            _slots.Add(path, slot);
            slot.Request = _thumbnailLoader.Request(
                path,
                ThumbnailPriority.Gallery,
                lease => CompleteThumbnail(path, slot, lease));
        }

        _stalePaths.Clear();
        foreach ((string path, GalleryItemSlot slot) in _slots)
        {
            if (!_visiblePaths.Contains(path))
            {
                _stalePaths.Add(path);
            }
        }

        foreach (string path in _stalePaths)
        {
            if (_slots.Remove(path, out GalleryItemSlot? slot))
            {
                slot.Dispose();
            }
        }

        _stalePaths.Clear();
        _visiblePaths.Clear();
    }

    private void CompleteThumbnail(string path, GalleryItemSlot slot, CachedBitmapLease lease)
    {
        if (!_slots.TryGetValue(path, out GalleryItemSlot? current) || !ReferenceEquals(current, slot))
        {
            lease.Dispose();
            return;
        }

        slot.Request?.Dispose();
        slot.Request = null;
        slot.SetSourceBitmap(lease);

        InvalidateVisual();
    }

    private void ScrollSelection(SelectionScrollAlignment alignment)
    {
        int selectedIndex = Array.FindIndex(
            _state.Entries,
            entry => string.Equals(entry.FullName, _state.SelectedPath, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0 || LayoutSize.Height <= 0.0f)
        {
            return;
        }

        if (alignment == SelectionScrollAlignment.Center)
        {
            if (_state.ScrollOffset.SetTarget(GetCenteredSelectionOffset(
                selectedIndex,
                _state.Entries.Length,
                ColumnCount,
                LayoutSize.Height,
                LayoutItemHeight)))
            {
                InvalidateVisual();
            }

            return;
        }

        int row = selectedIndex / ColumnCount;
        float top = PanelPadding + row * LayoutItemHeight;
        float bottom = top + LayoutItemHeight;
        float target = _state.ScrollOffset.TargetOffset;
        if (top < target)
        {
            target = top;
        }
        else if (bottom > target + LayoutSize.Height)
        {
            target = bottom - LayoutSize.Height;
        }

        _state.ScrollOffset.SetImmediate(target);
    }

    private void RevealSelectionIfPending()
    {
        if (_pendingSelectionScroll is not { } alignment || LayoutSize.Height <= 0.0f)
        {
            return;
        }

        ScrollSelection(alignment);
        _pendingSelectionScroll = null;
    }

    private void UpdateScrollMetrics()
    {
        _state.ScrollOffset.SetMaximum(MathF.Max(0.0f, ContentHeight - LayoutSize.Height));
        SetScrollbarMetrics();
    }

    private const float ScrollbarThickness = 12.0f;
    private const float ScrollbarGap = 2.0f;
    private SizeF LayoutSize => _orientation == UiOrientation.Vertical
        ? Bounds.Size
        : new SizeF(Bounds.Height, Bounds.Width);
    private float ContentWidth => MathF.Max(
        0.0f,
        LayoutSize.Width - ScrollbarThickness - ScrollbarGap - 2.0f * PanelPadding);
    private int ColumnCount => GetColumnCount(ContentWidth);
    private float ItemWidth => GetItemExtent(ContentWidth, ColumnCount);
    private float PhysicalItemWidth => _orientation == UiOrientation.Vertical
        ? ItemWidth
        : LayoutItemHeight - UiDesign.SmallSpacing;
    private float LayoutItemHeight => _orientation == UiOrientation.Vertical
        ? ItemHeight
        : MinimumItemWidth + UiDesign.SmallSpacing;
    private float LayoutMinimumItemWidth => _orientation == UiOrientation.Vertical
        ? MinimumItemWidth
        : ItemHeight - UiDesign.SmallSpacing;
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
    private float ContentHeight => 2.0f * PanelPadding + RowCount * LayoutItemHeight;
    private int RowCount => (_state.Entries.Length + ColumnCount - 1) / ColumnCount;

    private int GetColumnCount(float contentWidth)
    {
        float pitch = LayoutMinimumItemWidth + UiDesign.SmallSpacing;
        return Math.Max(1, (int)MathF.Floor((contentWidth + UiDesign.SmallSpacing) / pitch));
    }

    private static float GetItemExtent(float contentExtent, int itemCount) => MathF.Max(
        0.0f,
        (contentExtent - (itemCount - 1) * UiDesign.SmallSpacing) / itemCount);

    private void SetScrollOffset(float offset)
    {
        if (!_state.ScrollOffset.SetImmediate(offset))
        {
            return;
        }

        RefreshVisibleThumbnails();
        SetScrollbarMetrics();
        InvalidateVisual();
    }

    private void ApplyOrientationToState()
    {
        if (_state.Orientation == _orientation)
        {
            return;
        }

        _state.Orientation = _orientation;
        _state.ScrollOffset.SetImmediate(0.0f);
        _pendingSelectionScroll = SelectionScrollAlignment.EnsureVisible;
    }

    private void SetScrollbarMetrics()
    {
        _scrollbar.SetMetrics(ContentHeight, LayoutSize.Height, _state.ScrollOffset.Offset);
    }

    internal static PointF ToLayoutPoint(PointF point, UiOrientation orientation) =>
        orientation == UiOrientation.Vertical
        ? point
        : new PointF(point.Y, point.X);

    internal static RectangleF FromLayoutBounds(RectangleF bounds, UiOrientation orientation) =>
        orientation == UiOrientation.Vertical
        ? bounds
        : new RectangleF(bounds.Y, bounds.X, bounds.Height, bounds.Width);

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
        private SizeI _displayPixelSize;
        private float _displayDpi;
        private ID2D1Bitmap1? _displayBitmap;
        private CachedBitmapLease? _sourceLease;

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
        internal ID2D1Bitmap1? SourceBitmap => _sourceLease?.Bitmap.Bitmap;
        internal IDWriteTextLayout LabelLayout { get; private set; }

        internal void SetSourceBitmap(CachedBitmapLease lease)
        {
            _sourceLease?.Dispose();
            _sourceLease = lease;
            ClearDisplayBitmap();
        }

        internal ID2D1Bitmap1 GetDisplayBitmap(
            ID2D1DeviceContext scaleContext,
            float width,
            float height,
            float dpi)
        {
            ID2D1Bitmap1 source = SourceBitmap
                ?? throw new InvalidOperationException("The thumbnail has not been loaded.");
            var pixelSize = new SizeI(
                Math.Max(1, (int)MathF.Round(UiDpi.DipsToPixels(width, dpi))),
                Math.Max(1, (int)MathF.Round(UiDpi.DipsToPixels(height, dpi))));
            if (_displayBitmap is not null
                && _displayPixelSize == pixelSize
                && _displayDpi == dpi)
            {
                return _displayBitmap;
            }

            ID2D1Bitmap1 bitmap = D2DBitmapFactory.CreateScaled(
                scaleContext,
                source,
                pixelSize,
                dpi,
                ThumbnailSharpness);
            ClearDisplayBitmap();
            _displayBitmap = bitmap;
            _displayPixelSize = pixelSize;
            _displayDpi = dpi;
            return bitmap;
        }

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
            ClearDisplayBitmap();
            _sourceLease?.Dispose();
            LabelLayout.Dispose();
        }

        private void ClearDisplayBitmap()
        {
            _displayBitmap?.Dispose();
            _displayBitmap = null;
            _displayPixelSize = default;
            _displayDpi = 0.0f;
        }
    }
}
