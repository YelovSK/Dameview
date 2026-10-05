using System.Drawing;
using Dameview.Imaging.Loading;
using Dameview.Navigation;
using Dameview.Rendering;
using Dameview.Settings;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
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

    // Labels are laid out at widths rounded down to this step, so resizing the panel reshapes
    // them only when the width crosses a step rather than on every pointer move.
    private const float LabelWidthStep = 8.0f;
    private const float FooterHeight = 32.0f;
    private const float FooterPadding = 4.0f;
    private static readonly UiFont LabelFont = new(12.0f, Alignment: TextAlignment.Center, Ellipsis: true);
    private static readonly UiFont FooterFont = new(12.0f, Ellipsis: true);

    private ID2D1DeviceContext _thumbnailScaleContext;
    private readonly IImagePipeline _images;
    private readonly Action<string> _openImage;
    private readonly Action<string> _openInNewTab;
    private readonly WorkspaceDragGesture _drag;
    private readonly Action<string, PointF>? _contextMenuRequested;
    private readonly Scrollbar _scrollbar;
    private readonly Button _flattenButton;
    private readonly Button _recenterButton;
    private readonly Dictionary<string, GalleryItemSlot> _slots =
        new(StringComparer.OrdinalIgnoreCase);
    // A scratch buffer, empty between calls. It is a field only so that refreshing on every
    // scrolled frame reuses its capacity instead of allocating a set per frame.
    private readonly HashSet<string> _visiblePaths = new(StringComparer.OrdinalIgnoreCase);
    // A ConditionalWeakTable could tie these states to tab reachability, but explicit
    // disposal keeps this ownership visible and deterministic.
    private readonly Dictionary<ViewerTab, GalleryPanelState> _tabStates = [];
    private GalleryPanelState _state = new();
    private string? _selectedPath;
    private string _footerText = string.Empty;
    private GalleryThumbnailSize _thumbnailSize = GalleryThumbnailSize.Medium;
    private UiOrientation _orientation = UiOrientation.Vertical;
    private SelectionScrollAlignment? _pendingSelectionScroll;
    private int _hoveredIndex = -1;
    private string? _pressedPath;

    internal GalleryPanel(
        ID2D1DeviceContext deviceContext,
        IImagePipeline images,
        Action<string> openImage,
        Action<string> openInNewTab,
        Action toggleFlattenFolder,
        Action<string, WorkspaceDragEvent>? dragPointer = null,
        Action<string, PointF>? contextMenuRequested = null)
    {
        _thumbnailScaleContext = D2DBitmapFactory.CreateOffscreenContext(deviceContext);
        _images = images;
        _openImage = openImage;
        _openInNewTab = openInNewTab;
        _drag = new WorkspaceDragGesture(drag => dragPointer?.Invoke(_pressedPath!, drag));
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

    /// <param name="tab">The active tab, whose gallery keeps its own scroll position.</param>
    internal void ApplyState(ViewerTab tab, ViewerSessionState session)
    {
        GalleryPanelState state = GetTabState(tab);
        string footerText = GetFooterText(session);
        if (_footerText != footerText || _flattenButton.IsSelected != session.FlattensFolder)
        {
            _footerText = footerText;
            _flattenButton.IsSelected = session.FlattensFolder;
            InvalidateVisual();
        }

        bool stateChanged = !ReferenceEquals(_state, state);
        bool entriesChanged = !ReferenceEquals(state.Entries, session.FolderEntries);
        bool selectionChanged = !SamePath(_selectedPath, session.RequestedPath);
        if (!stateChanged && !entriesChanged && !selectionChanged)
        {
            RefreshVisibleThumbnails();
            return;
        }

        if (stateChanged)
        {
            _state = state;
            _pendingSelectionScroll = null;
            ApplyOrientationToState();
        }

        if (stateChanged || entriesChanged)
        {
            SetHoveredIndex(-1);
        }

        if (entriesChanged)
        {
            _state.Entries = session.FolderEntries;
            _pendingSelectionScroll = SelectionScrollAlignment.EnsureVisible;
        }

        _selectedPath = session.RequestedPath;
        SyncScroll();
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
        InvalidateLayout();
    }

    protected override SizeF MeasureCore(SizeF availableSize) => new(
        MathF.Min(DefaultSizeDips, MathF.Max(0.0f, availableSize.Width)),
        MathF.Max(0.0f, availableSize.Height));

    protected override void ArrangeCore(SizeF finalSize)
    {
        _scrollbar.Arrange(Layout.ScrollbarBounds);
        _flattenButton.Arrange(GetFooterButtonBounds(0.0f));
        _recenterButton.Arrange(GetFooterButtonBounds(Bounds.Width - FooterHeight));
        SyncScroll();
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
                    return new UiPointerResult(Consumed: true, NeedsRepaint: _drag.Move(input.Position));
                }

                int hovered = HitTestItem(input.Position);
                bool changed = _hoveredIndex != hovered;
                SetHoveredIndex(hovered);
                return new UiPointerResult(Consumed: true, NeedsRepaint: changed);

            case WindowPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                _pressedPath = HitTestPath(input.Position);
                _drag.Press(input.Position);
                return new UiPointerResult(
                    Consumed: true,
                    NeedsRepaint: true,
                    CapturePointer: _pressedPath is not null);

            case WindowPointerEventKind.Pressed when input.Button == PointerButton.Middle:
                if (HitTestPath(input.Position) is { } middleClicked)
                {
                    _openInNewTab(middleClicked);
                }

                return new UiPointerResult(Consumed: true);

            case WindowPointerEventKind.Pressed when input.Button == PointerButton.Secondary:
                if (HitTestPath(input.Position) is { } rightClicked)
                {
                    _contextMenuRequested?.Invoke(rightClicked, input.Position);
                }

                return new UiPointerResult(Consumed: true);

            case WindowPointerEventKind.Released:
                string? pressedPath = _pressedPath;
                bool wasDragging = _drag.Release(input.Position);
                _pressedPath = null;
                if (!wasDragging && pressedPath is not null && SamePath(pressedPath, HitTestPath(input.Position)))
                {
                    _openImage(pressedPath);
                }

                return new UiPointerResult(Consumed: true, NeedsRepaint: pressedPath is not null);

            case WindowPointerEventKind.Cancelled:
                bool wasPressed = _pressedPath is not null;
                _drag.Cancel(input.Position);
                _pressedPath = null;
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
        foreach (ViewerTab tab in _tabStates.Keys)
        {
            tab.Disposed -= HandleTabDisposed;
        }

        _tabStates.Clear();
        ClearSlots();
        _thumbnailScaleContext.Dispose();
    }

    private GalleryPanelState GetTabState(ViewerTab tab)
    {
        if (!_tabStates.TryGetValue(tab, out GalleryPanelState? state))
        {
            state = new GalleryPanelState();
            _tabStates.Add(tab, state);
            tab.Disposed += HandleTabDisposed;
        }

        return state;
    }

    private void HandleTabDisposed(ViewerTab tab) => _tabStates.Remove(tab);

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        if (!_state.ScrollOffset.Update(context))
        {
            return false;
        }

        OnScrolled();
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

    private string? HitTestPath(PointF position)
    {
        int index = HitTestItem(position);
        return index >= 0 ? _state.Entries[index].FullName : null;
    }

    private static bool SamePath(string? first, string? second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    // File names are often cut short by the label, so the tooltip carries the whole name.
    private void SetHoveredIndex(int index)
    {
        if (_hoveredIndex == index)
        {
            return;
        }

        _hoveredIndex = index;
        ToolTip = index >= 0
            ? new UiToolTip(
                _state.Entries[index].Name,
                Layout.GetItemBounds(index, _state.ScrollOffset.Offset))
            : null;
    }

    private void DrawItem(in UiDrawContext context, RectangleF itemBounds, int index)
    {
        FolderEntry entry = _state.Entries[index];
        bool selected = SamePath(entry.FullName, _selectedPath);
        bool pressed = SamePath(entry.FullName, _pressedPath);
        if (selected || pressed || index == _hoveredIndex)
        {
            Color4 color = selected
                ? context.Palette.Accent
                : pressed ? context.Palette.ControlPressed : context.Palette.ControlHover;
            context.FillRoundedRectangle(
                new RoundedRectangle(itemBounds, UiDesign.ControlCornerRadius, UiDesign.ControlCornerRadius),
                color,
                selected ? 0.28f : 1.0f);
        }

        RectangleF imageBounds = GalleryLayout.GetThumbnailBounds(itemBounds);
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

        RectangleF label = GalleryLayout.GetLabelBounds(itemBounds);
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
        float scale = MathF.Min(
            bounds.Width / source.PixelSize.Width,
            bounds.Height / source.PixelSize.Height);
        float width = source.PixelSize.Width * scale;
        float height = source.PixelSize.Height * scale;
        // Building a scaled copy on every resize step is too slow. During a resize we use the
        // copy we already have if it still fits, and stretch the original if it doesn't.
        ID2D1Bitmap1? bitmap = Root?.IsResizing == true
            ? slot.TryGetDisplayBitmap(width, height, context.Dpi)
            : slot.GetDisplayBitmap(_thumbnailScaleContext, width, height, context.Dpi);
        if (bitmap is null)
        {
            context.DrawBitmapFitted(source, bounds);
            return;
        }

        context.DrawBitmap(
            bitmap,
            new Rect(
                bounds.X + ((bounds.Width - bitmap.Size.Width) / 2.0f),
                bounds.Y + ((bounds.Height - bitmap.Size.Height) / 2.0f),
                bitmap.Size.Width,
                bitmap.Size.Height));
    }

    private void RefreshVisibleThumbnails()
    {
        if (!IsVisible)
        {
            ClearSlots();
            return;
        }

        (int first, int lastExclusive) = Layout.GetVisibleRange(_state.ScrollOffset.Offset);
        for (int index = first; index < lastExclusive; index++)
        {
            string path = _state.Entries[index].FullName;
            _visiblePaths.Add(path);
            if (_slots.ContainsKey(path))
            {
                continue;
            }

            var slot = new GalleryItemSlot();
            _slots.Add(path, slot);
            slot.Request = _images.RequestThumbnail(
                path,
                ImagePriority.Gallery,
                lease => CompleteThumbnail(path, slot, lease));
        }

        foreach ((string path, GalleryItemSlot slot) in _slots)
        {
            if (!_visiblePaths.Contains(path))
            {
                _slots.Remove(path);
                slot.Dispose();
            }
        }

        _visiblePaths.Clear();
    }

    private void CompleteThumbnail(string path, GalleryItemSlot slot, CachedBitmapLease lease)
    {
        if (!_slots.TryGetValue(path, out GalleryItemSlot? current) || !ReferenceEquals(current, slot))
        {
            lease.Dispose();
            return;
        }

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
        int selected = Array.FindIndex(_state.Entries, entry => SamePath(entry.FullName, _selectedPath));
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

    private void SyncScroll()
    {
        _state.ScrollOffset.SetMaximum(Layout.MaximumScrollOffset);
        RevealSelectionIfPending();
        RefreshVisibleThumbnails();
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
        if (_state.ScrollOffset.SetImmediate(offset))
        {
            OnScrolled();
        }
    }

    // Items move out from under the pointer, so hover waits for the next pointer move.
    private void OnScrolled()
    {
        SetHoveredIndex(-1);
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
