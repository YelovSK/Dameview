using System.Drawing;
using Dameview.Imaging.Loading;
using Dameview.Navigation;
using Dameview.Rendering;
using Dameview.Settings;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.UI.Presentation;
using Dameview.UI.Workspace;
using Dameview.Viewing;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class GalleryPanel : UiElement, IDisposable
{
    internal const float DefaultSizeDips = AppSettings.DefaultGallerySizeDips;

    private const float DragThresholdDips = 4.0f;
    // Labels are laid out at widths rounded down to this step, so resizing the panel reshapes
    // them only when the width crosses a step rather than on every pointer move.
    private const float LabelWidthStep = 8.0f;
    private const float FooterHeight = 32.0f;
    private const float FooterPadding = 4.0f;
    private static readonly UiFont LabelFont = new(12.0f, Alignment: TextAlignment.Center, Ellipsis: true);
    private static readonly UiFont FooterFont = new(12.0f, Ellipsis: true);

    private ID2D1DeviceContext _thumbnailScaleContext;
    private readonly IThumbnailImageLoader _thumbnailLoader;
    private readonly Action<string> _openImage;
    private readonly Action<string> _openInNewTab;
    private readonly Action<string, WorkspaceDragEvent>? _dragPointer;
    private readonly Action<string, PointF>? _contextMenuRequested;
    private readonly Scrollbar _scrollbar;
    private readonly Button _flattenButton;
    private readonly Button _recenterButton;
    private readonly Dictionary<string, GalleryItemSlot> _slots =
        new(StringComparer.OrdinalIgnoreCase);
    // Scratch buffers, empty between calls. They are fields only so that refreshing on every
    // scrolled frame reuses their capacity instead of allocating two collections per frame.
    private readonly HashSet<string> _visiblePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _stalePaths = [];
    private GalleryPanelState _state = new();
    private string _footerText = string.Empty;
    private GalleryThumbnailSize _thumbnailSize = GalleryThumbnailSize.Medium;
    private UiOrientation _orientation = UiOrientation.Vertical;
    private SelectionScrollAlignment? _pendingSelectionScroll;
    private int _hoveredIndex = -1;
    private int _pressedIndex = -1;
    private string? _pressedPath;
    private PointF _pressPosition;
    private bool _dragging;

    internal GalleryPanel(
        ID2D1DeviceContext deviceContext,
        IThumbnailImageLoader thumbnailLoader,
        Action<string> openImage,
        Action<string> openInNewTab,
        Action toggleFlattenFolder,
        Action<string, WorkspaceDragEvent>? dragPointer = null,
        Action<string, PointF>? contextMenuRequested = null)
    {
        _thumbnailScaleContext = D2DBitmapFactory.CreateOffscreenContext(deviceContext);
        _thumbnailLoader = thumbnailLoader;
        _openImage = openImage;
        _openInNewTab = openInNewTab;
        _dragPointer = dragPointer;
        _contextMenuRequested = contextMenuRequested;
        _scrollbar = new Scrollbar(SetScrollOffset);
        _flattenButton = new Button(
            UiTypography.OpenFolderIcon,
            toggleFlattenFolder,
            fontFamily: UiTypography.IconFontFamily,
            fontSize: 16.0f)
        {
            ToolTip = new("Include images from subfolders"),
        };
        _recenterButton = new Button(
            UiTypography.LocateIcon,
            CenterSelection,
            fontFamily: UiTypography.IconFontFamily,
            fontSize: 16.0f)
        {
            ToolTip = new("Scroll to the current image"),
        };
        AddChild(_scrollbar);
        AddChild(_flattenButton);
        AddChild(_recenterButton);
    }

    internal override bool PreservesFocusOnPointerPress => true;
    internal override WindowCursor Cursor => _hoveredIndex >= 0 ? WindowCursor.Pointer : WindowCursor.Default;

    private GalleryLayout Layout =>
        new(GridBounds.Size, _orientation, _thumbnailSize, _state.Entries.Length);

    private RectangleF GridBounds =>
        new(0.0f, 0.0f, Bounds.Width, MathF.Max(0.0f, Bounds.Height - FooterHeight));

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
        SetHoveredIndex(-1);
        _pressedIndex = -1;
        UpdateScrollMetrics();
        RefreshVisibleThumbnails();
        InvalidateVisual();
    }

    internal void ApplyState(ViewerSessionState session)
    {
        string footerText = GetFooterText(session);
        if (_footerText != footerText || _flattenButton.IsSelected != session.FlattensFolder)
        {
            _footerText = footerText;
            _flattenButton.IsSelected = session.FlattensFolder;
            InvalidateVisual();
        }

        FolderEntry[] entries = session.FolderEntries;
        string? selectedPath = session.RequestedPath;
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
            SetHoveredIndex(-1);
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

    internal void RecreateDeviceResources(ID2D1DeviceContext deviceContext)
    {
        foreach (GalleryItemSlot slot in _slots.Values)
        {
            slot.ClearDisplayBitmap();
        }

        _thumbnailScaleContext.Dispose();
        _thumbnailScaleContext = D2DBitmapFactory.CreateOffscreenContext(deviceContext);
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
        SetHoveredIndex(-1);
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
        _scrollbar.Arrange(Layout.ScrollbarBounds);
        _flattenButton.Arrange(GetFooterButtonBounds(0.0f));
        _recenterButton.Arrange(GetFooterButtonBounds(Bounds.Width - FooterHeight));
        SetScrollbarMetrics();
    }

    private RectangleF GetFooterButtonBounds(float x) => new(
        x + FooterPadding,
        GridBounds.Bottom + FooterPadding,
        FooterHeight - (2.0f * FooterPadding),
        FooterHeight - (2.0f * FooterPadding));

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

        RectangleF grid = GridBounds;
        GalleryLayout layout = Layout;
        float scrollOffset = _state.ScrollOffset.Offset;
        (int first, int lastExclusive) = layout.GetVisibleRange(scrollOffset);
        context.PushClip(grid);
        for (int index = first; index < lastExclusive; index++)
        {
            DrawItem(context, layout.GetItemBounds(index, scrollOffset), index);
        }

        context.PopClip();
        context.FillRoundedRectangle(
            new RoundedRectangle(new RectangleF(0.0f, grid.Bottom, Bounds.Width, 1.0f), 0.0f, 0.0f),
            context.Palette.SurfaceBorder);
        float textX = FooterHeight + FooterPadding;
        context.DrawText(
            _footerText,
            FooterFont,
            new Rect(textX, grid.Bottom, MathF.Max(0.0f, Bounds.Width - textX - FooterHeight), FooterHeight),
            context.Palette.SecondaryText,
            DrawTextOptions.Clip);
    }

    private static string GetFooterText(ViewerSessionState session)
    {
        int count = session.FolderEntries.Length;
        string images = count == 1 ? "1 image" : $"{count:N0} images";
        TimeSpan duration = session.ScanDuration;
        return session.IsScanning ? $"{images} · Scanning…"
            : duration < TimeSpan.FromSeconds(1) ? $"{images} · {duration.TotalMilliseconds:0} ms"
            : $"{images} · {duration.TotalSeconds:0.00} s";
    }

    internal override UiPointerResult OnPointerEvent(in WindowPointerEvent input)
    {
        switch (input.Kind)
        {
            case WindowPointerEventKind.Moved:
                if (_pressedPath is not null)
                {
                    if (!_dragging && HasCrossedDragThreshold(input.Position))
                    {
                        _dragging = true;
                        RaiseDrag(_pressedPath, WorkspaceDragEventKind.Started, input.Position);
                    }

                    if (_dragging)
                    {
                        RaiseDrag(_pressedPath, WorkspaceDragEventKind.Moved, input.Position);
                    }

                    return new UiPointerResult(Consumed: true, NeedsRepaint: _dragging);
                }

                int hovered = HitTestItem(input.Position);
                bool changed = _hoveredIndex != hovered;
                SetHoveredIndex(hovered);
                return new UiPointerResult(Consumed: true, NeedsRepaint: changed);

            case WindowPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                _pressedIndex = HitTestItem(input.Position);
                _pressedPath = _pressedIndex >= 0 ? _state.Entries[_pressedIndex].FullName : null;
                _pressPosition = input.Position;
                _dragging = false;
                return new UiPointerResult(
                    Consumed: true,
                    NeedsRepaint: true,
                    CapturePointer: _pressedIndex >= 0);

            case WindowPointerEventKind.Pressed when input.Button == PointerButton.Middle:
                int middleClicked = HitTestItem(input.Position);
                if (middleClicked >= 0)
                {
                    _openInNewTab(_state.Entries[middleClicked].FullName);
                }

                return new UiPointerResult(Consumed: true);

            case WindowPointerEventKind.Pressed when input.Button == PointerButton.Secondary:
                int rightClicked = HitTestItem(input.Position);
                if (rightClicked >= 0)
                {
                    _contextMenuRequested?.Invoke(_state.Entries[rightClicked].FullName, input.Position);
                }

                return new UiPointerResult(Consumed: true);

            case WindowPointerEventKind.Released:
                int pressed = _pressedIndex;
                string? pressedPath = _pressedPath;
                bool wasDragging = _dragging;
                ClearPressState();
                if (wasDragging && pressedPath is not null)
                {
                    RaiseDrag(pressedPath, WorkspaceDragEventKind.Completed, input.Position);
                }
                else if (pressed >= 0 && pressed == HitTestItem(input.Position))
                {
                    _openImage(_state.Entries[pressed].FullName);
                }

                return new UiPointerResult(Consumed: true, NeedsRepaint: pressed >= 0);

            case WindowPointerEventKind.Cancelled:
                bool wasPressed = _pressedIndex >= 0;
                if (_dragging && _pressedPath is { } cancelledPath)
                {
                    RaiseDrag(cancelledPath, WorkspaceDragEventKind.Cancelled, input.Position);
                }

                ClearPressState();
                return new UiPointerResult(Consumed: true, NeedsRepaint: wasPressed);

            case WindowPointerEventKind.Wheel:
                // A notch scrolls half an item, which is small enough to stay readable.
                float step = -input.WheelDelta / 120.0f * Layout.ItemScrollExtent / 2.0f;
                return new UiPointerResult(
                    Consumed: true,
                    NeedsRepaint: _state.ScrollOffset.ScrollBy(step));

            default:
                return new UiPointerResult(Consumed: true);
        }
    }

    public void Dispose()
    {
        ClearSlots();
        _thumbnailScaleContext.Dispose();
    }

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        if (!_state.ScrollOffset.Update(context))
        {
            return false;
        }

        // Items move out from under their tooltip; the next pointer move brings it back.
        ToolTip = null;
        RefreshVisibleThumbnails();
        SetScrollbarMetrics();
        InvalidateVisual();
        return true;
    }

    protected override void OnVisualStateChanged()
    {
        if (!HasVisualState(UiVisualState.Hovered))
        {
            SetHoveredIndex(-1);
        }
    }

    private int HitTestItem(PointF position) => Layout.HitTest(position, _state.ScrollOffset.Offset);

    // File names are often cut short by the label, so the tooltip carries the whole name.
    private void SetHoveredIndex(int index)
    {
        _hoveredIndex = index;
        ToolTip = index >= 0
            ? new UiToolTip(
                _state.Entries[index].Name,
                Layout.GetItemBounds(index, _state.ScrollOffset.Offset))
            : null;
    }

    private void RaiseDrag(string path, WorkspaceDragEventKind kind, PointF position) =>
        _dragPointer?.Invoke(path, new WorkspaceDragEvent(kind, position));

    private void ClearPressState()
    {
        _pressedIndex = -1;
        _pressedPath = null;
        _dragging = false;
    }

    private void DrawItem(in UiDrawContext context, RectangleF itemBounds, int index)
    {
        FolderEntry entry = _state.Entries[index];
        bool selected = string.Equals(
            entry.FullName,
            _state.SelectedPath,
            StringComparison.OrdinalIgnoreCase);
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

        RectangleF imageBounds = GalleryLayout.GetThumbnailBounds(
            itemBounds,
            GalleryItemSlot.LabelHeight);
        _slots.TryGetValue(entry.FullName, out GalleryItemSlot? slot);
        if (slot?.SourceBitmap is not null)
        {
            DrawThumbnail(context, slot, imageBounds);
        }
        else
        {
            context.FillRoundedRectangle(
                new RoundedRectangle(imageBounds, UiDesign.ControlCornerRadius, UiDesign.ControlCornerRadius),
                context.Palette.OverlaySurface);
        }

        RectangleF label = GalleryLayout.GetLabelBounds(itemBounds, GalleryItemSlot.LabelHeight);
        float width = MathF.Floor(label.Width / LabelWidthStep) * LabelWidthStep;
        context.DrawText(
            entry.Name,
            LabelFont,
            new Rect(label.X + ((label.Width - width) / 2.0f), label.Y, width, label.Height),
            context.Palette.PrimaryText,
            DrawTextOptions.Clip);
    }

    private void DrawThumbnail(in UiDrawContext context, GalleryItemSlot slot, RectangleF bounds)
    {
        ID2D1Bitmap1 source = slot.SourceBitmap!;
        ID2D1Bitmap1 bitmap;
        float width;
        float height;
        if (Root?.IsResizing == true)
        {
            // Stretching the source avoids rebuilding a GPU bitmap on every size step.
            bitmap = source;
            float scale = MathF.Min(
                bounds.Width / source.Size.Width,
                bounds.Height / source.Size.Height);
            width = source.Size.Width * scale;
            height = source.Size.Height * scale;
        }
        else
        {
            float scale = MathF.Min(
                bounds.Width / source.PixelSize.Width,
                bounds.Height / source.PixelSize.Height);
            bitmap = slot.GetDisplayBitmap(
                _thumbnailScaleContext,
                source.PixelSize.Width * scale,
                source.PixelSize.Height * scale,
                context.Dpi);
            width = bitmap.Size.Width;
            height = bitmap.Size.Height;
        }

        context.DrawBitmap(
            bitmap,
            new Rect(
                bounds.X + ((bounds.Width - width) / 2.0f),
                bounds.Y + ((bounds.Height - height) / 2.0f),
                width,
                height),
            new Rect(0.0f, 0.0f, bitmap.Size.Width, bitmap.Size.Height));
    }

    private void RefreshVisibleThumbnails()
    {
        if (!IsVisible)
        {
            ClearSlots();
            return;
        }

        GalleryLayout layout = Layout;
        (int first, int lastExclusive) = layout.GetVisibleRange(_state.ScrollOffset.Offset);
        HashSet<string> visible = _visiblePaths;
        for (int index = first; index < lastExclusive; index++)
        {
            FolderEntry entry = _state.Entries[index];
            string path = entry.FullName;
            visible.Add(path);
            if (_slots.ContainsKey(path))
            {
                continue;
            }

            var slot = new GalleryItemSlot();
            _slots.Add(path, slot);
            slot.Request = _thumbnailLoader.Request(
                path,
                ThumbnailPriority.Gallery,
                lease => CompleteThumbnail(path, slot, lease));
        }

        foreach ((string path, GalleryItemSlot slot) in _slots)
        {
            if (!visible.Contains(path))
            {
                _stalePaths.Add(path);
            }
        }

        foreach (string path in _stalePaths)
        {
            if (_slots.Remove(path, out GalleryItemSlot? stale))
            {
                stale.Dispose();
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

    private void RevealSelectionIfPending()
    {
        GalleryLayout layout = Layout;
        // An unsized panel is about to be arranged, so the request waits for that call.
        if (_pendingSelectionScroll is not { } alignment || layout.ViewportLength <= 0.0f)
        {
            return;
        }

        // A selection outside the folder may never appear, and reviving the request later
        // would scroll somewhere the user has long stopped expecting. Reveal it now or never.
        _pendingSelectionScroll = null;
        int selected = Array.FindIndex(
            _state.Entries,
            entry => string.Equals(
                entry.FullName,
                _state.SelectedPath,
                StringComparison.OrdinalIgnoreCase));
        if (selected < 0)
        {
            return;
        }

        if (alignment == SelectionScrollAlignment.Center)
        {
            if (_state.ScrollOffset.SetTarget(layout.GetCenteredOffset(selected)))
            {
                InvalidateVisual();
            }

            return;
        }

        _state.ScrollOffset.SetImmediate(
            layout.GetRevealOffset(selected, _state.ScrollOffset.TargetOffset));
    }

    private void UpdateScrollMetrics()
    {
        _state.ScrollOffset.SetMaximum(Layout.MaximumScrollOffset);
        SetScrollbarMetrics();
    }

    private void SetScrollbarMetrics()
    {
        GalleryLayout layout = Layout;
        _scrollbar.SetMetrics(
            layout.ContentLength,
            layout.ViewportLength,
            _state.ScrollOffset.Offset);
    }

    private void SetScrollOffset(float offset)
    {
        if (!_state.ScrollOffset.SetImmediate(offset))
        {
            return;
        }

        ToolTip = null;
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

    private bool HasCrossedDragThreshold(PointF position) =>
        MathF.Abs(position.X - _pressPosition.X) >= DragThresholdDips
        || MathF.Abs(position.Y - _pressPosition.Y) >= DragThresholdDips;

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
}
