using System.Drawing;
using Dameview.Commands;
using Dameview.Imaging.Loading;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.UI.Panels;
using Dameview.Viewing;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Dameview.UI.Workspace;

// Presents the active tab of one viewer pane. Shared application chrome remains in ViewerUi.
internal sealed class ViewerPaneView : UiElement, IDisposable
{
    private readonly ImagePanel _imagePanel;
    private readonly ViewerTabStrip _viewerTabs;
    private readonly EmptyStatePanel _emptyStatePanel;
    private readonly Overlay _contentOverlay;
    private readonly ToolbarPanel _toolbarPanel;
    private readonly StatusPanel _statusPanel;
    private readonly ActivePaneIndicator _activePaneIndicator;
    private readonly Action<ViewerPane, ViewerTabInfo?, RectangleF> _hoveredTabChanged;
    private ViewerSessionState _state;
    private bool _chromeVisible = true;

    internal ViewerPaneView(
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        ViewerPane pane,
        IViewerCommands commands,
        Action<int> selectTab,
        Action<int> closeTab,
        Action addTab,
        Action openFile,
        Action showSettings,
        Action<ViewerPane, ViewerTabInfo?, RectangleF> hoveredTabChanged,
        Action<ViewerPane, int, WorkspaceDragEvent> tabDragPointer,
        TimeProvider? timeProvider = null)
    {
        Pane = pane;
        _hoveredTabChanged = hoveredTabChanged;
        ViewerTab tab = pane.ActiveTab;
        ViewerSession session = tab.Session;
        _state = session.State;
        _imagePanel = new ImagePanel(
            deviceContext,
            session.Viewport,
            session.Animator,
            timeProvider);
        _viewerTabs = new ViewerTabStrip(
            directWriteFactory,
            [new ViewerTabInfo("Dameview", null)],
            0,
            selectTab,
            closeTab,
            addTab,
            HandleHoveredTabChanged,
            (index, input) => tabDragPointer(Pane, index, TranslateTabStripEvent(input)));
        _emptyStatePanel = new EmptyStatePanel(
            directWriteFactory,
            deviceContext,
            openFile,
            showSettings);
        _contentOverlay = new Overlay(_imagePanel, _emptyStatePanel);
        _toolbarPanel = new ToolbarPanel(directWriteFactory, commands, pane, showSettings);
        _statusPanel = new StatusPanel(directWriteFactory);
        _activePaneIndicator = new ActivePaneIndicator { IsVisible = false };

        AddChild(_viewerTabs);
        AddChild(_contentOverlay);
        AddChild(_toolbarPanel);
        AddChild(_statusPanel);
        AddChild(_activePaneIndicator);

        UpdateChromeVisibility();
        if (_state.DisplayedImage is { } displayed)
        {
            ApplyDisplayedImage(displayed);
        }
    }

    internal ViewerPane Pane { get; }
    internal bool HasImage => _state.DisplayedImage is not null;
    internal bool HasStatus => HasImage
        || _state.Message is not null
        || _state.FolderError is not null;
    internal RectangleF ContentBounds { get; private set; }
    internal UiElement FocusScope => _toolbarPanel.IsVisible ? _toolbarPanel : _emptyStatePanel;

    internal TimeSpan? NextAnimationFrameDelay => _imagePanel.NextAnimationFrameDelay;
    internal RectangleF TabStripBounds => _viewerTabs.GetBoundsRelativeTo(this);
    internal override bool ObservePointerMoves => true;

    internal void SetChromeVisible(bool visible)
    {
        if (_chromeVisible == visible)
        {
            return;
        }

        _chromeVisible = visible;
        UpdateChromeVisibility();
        if (!visible)
        {
            _statusPanel.SetPointerNear(false);
        }
    }

    internal bool ShowActivePaneIndicator
    {
        get => _activePaneIndicator.IsVisible;
        set => _activePaneIndicator.IsVisible = value;
    }

    internal string? SettingsError
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            _statusPanel.IsVisible = _chromeVisible && HasStatus;
            InvalidateVisual();
        }
    }

    private void UpdateChromeVisibility()
    {
        bool hasImage = HasImage;
        _imagePanel.IsVisible = hasImage;
        _emptyStatePanel.IsVisible = !hasImage && !_state.IsLoading;
        // A single tab that holds nothing is not worth a strip to switch between.
        _viewerTabs.IsVisible = _chromeVisible && (Pane.Count > 1 || hasImage);
        _toolbarPanel.IsVisible = _chromeVisible && hasImage;
        _statusPanel.IsVisible = _chromeVisible && HasStatus;
    }

    internal PointF GetImageViewportPoint(PointF panePoint, float dpi)
    {
        RectangleF imageBounds = _imagePanel.GetBoundsRelativeTo(this);
        return new PointF(
            UiDpi.DipsToPixels(panePoint.X - imageBounds.X, dpi),
            UiDpi.DipsToPixels(panePoint.Y - imageBounds.Y, dpi));
    }

    internal void BindTab(ViewerTab tab)
    {
        ViewerSession session = tab.Session;
        _imagePanel.Bind(session.Viewport, session.Animator);
    }

    internal void ApplyState(ViewerSessionState state, bool clearPointer)
    {
        bool displayedImageChanged = !ReferenceEquals(_state.DisplayedImage, state.DisplayedImage);
        _state = state;
        if (displayedImageChanged && state.DisplayedImage is { } displayed)
        {
            if (clearPointer)
            {
                Root?.ClearPointer();
            }

            ApplyDisplayedImage(displayed);
        }

        UpdateChromeVisibility();
        InvalidateVisual();
    }

    internal void ApplyTabs(IReadOnlyList<ViewerTabInfo> tabs, int selectedIndex)
    {
        _viewerTabs.SetTabs(tabs, selectedIndex);
        UpdateChromeVisibility();
        InvalidateLayout();
    }

    internal int GetTabInsertionIndex(PointF panePoint)
    {
        RectangleF bounds = TabStripBounds;
        return _viewerTabs.GetInsertionIndex(new PointF(panePoint.X - bounds.X, panePoint.Y - bounds.Y));
    }

    internal RectangleF GetTabInsertionMarkerBounds(int insertionIndex)
    {
        RectangleF bounds = _viewerTabs.GetInsertionMarkerBounds(insertionIndex);
        bounds.Offset(TabStripBounds.Location);
        return bounds;
    }

    internal void UpdateStatus()
    {
        if (!HasStatus)
        {
            return;
        }

        string? animationError = _imagePanel.AnimationError is { } exception
            ? $"Animation stopped: {exception.Message}"
            : null;
        string? message = animationError ?? _state.Message;
        if (message is null && _state.FolderError is { } folderError)
        {
            message = $"Image opened, but its folder could not be read: {folderError}";
        }

        _statusPanel.SetStatus(new ViewerStatus(
            Path.GetFileName(_state.RequestedPath) ?? string.Empty,
            _state.DisplayedImage?.Representation.Width ?? 0,
            _state.DisplayedImage?.Representation.Height ?? 0,
            _state.CurrentEntry?.Length,
            _imagePanel.ZoomPercentage,
            message,
            animationError is not null || _state.IsError || _state.FolderError is not null));
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float tabHeight = _viewerTabs.IsVisible
            ? _viewerTabs.Measure(new SizeF(availableSize.Width, ViewerTabStrip.HeightDips)).Height
            : 0.0f;
        var contentSize = new SizeF(
            availableSize.Width,
            MathF.Max(0.0f, availableSize.Height - tabHeight));
        _contentOverlay.Measure(contentSize);
        if (_statusPanel.IsVisible)
        {
            _statusPanel.Measure(new SizeF(
                MathF.Max(0.0f, contentSize.Width - (2.0f * UiDesign.WindowMargin)),
                StatusPanel.HeightDips));
        }

        return availableSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        float tabHeight = _viewerTabs.IsVisible ? ViewerTabStrip.HeightDips : 0.0f;
        _viewerTabs.Arrange(new RectangleF(0.0f, 0.0f, finalSize.Width, tabHeight));
        ContentBounds = new RectangleF(
            0.0f,
            tabHeight,
            finalSize.Width,
            MathF.Max(0.0f, finalSize.Height - tabHeight));
        _contentOverlay.Arrange(ContentBounds);

        ViewerLayout layout = ViewerLayout.Calculate(
            ContentBounds.Size,
            showStatus: _chromeVisible && HasStatus,
            showToolbar: _toolbarPanel.IsVisible,
            statusWidthDips: _statusPanel.DesiredSize.Width,
            statusHeightDips: _statusPanel.DesiredSize.Height,
            toolbarWidthDips: ToolbarPanel.WidthDips);
        RectangleF status = layout.Status;
        status.Offset(ContentBounds.Location);
        _statusPanel.Arrange(status);
        RectangleF toolbar = layout.Toolbar;
        toolbar.Offset(ContentBounds.Location);
        _toolbarPanel.Arrange(toolbar);
        _activePaneIndicator.Arrange(new RectangleF(PointF.Empty, finalSize));
    }

    protected override bool HitTestCore(PointF position) => false;

    protected override void ObservePointerMove(in WindowPointerEvent input)
    {
        bool insidePane = input.Position.X >= 0.0f
            && input.Position.X < Bounds.Width
            && input.Position.Y >= 0.0f
            && input.Position.Y < Bounds.Height;
        _statusPanel.SetPointerNear(
            insidePane && input.Position.Y >= Bounds.Height - StatusPanel.HeightDips - 24.0f);
        _toolbarPanel.SetPointerNear(
            insidePane
            && input.Position.Y <= ContentBounds.Y
                + UiDesign.WindowMargin
                + UiDesign.ToolbarHeight
                + 28.0f);
    }

    internal void RecreateDeviceResources(ID2D1DeviceContext deviceContext)
    {
        _imagePanel.RecreateDeviceResources(deviceContext);
        _emptyStatePanel.RecreateDeviceResources(deviceContext);

        // Rebinds from the representation, without reloading.
        if (_state.DisplayedImage is { } displayed)
        {
            ApplyDisplayedImage(displayed);
        }
    }

    public void Dispose()
    {
        _viewerTabs.Dispose();
        _toolbarPanel.Dispose();
        _statusPanel.Dispose();
        _emptyStatePanel.Dispose();
        _imagePanel.Dispose();
    }

    private void HandleHoveredTabChanged(ViewerTabInfo? tab, RectangleF tabBounds)
    {
        if (tab is not null)
        {
            RectangleF stripBounds = _viewerTabs.GetBoundsRelativeTo(this);
            tabBounds.Offset(stripBounds.Location);
        }

        _hoveredTabChanged(Pane, tab, tabBounds);
    }

    private WorkspaceDragEvent TranslateTabStripEvent(WorkspaceDragEvent input)
    {
        RectangleF stripBounds = TabStripBounds;
        return input with
        {
            Position = new PointF(input.Position.X + stripBounds.X, input.Position.Y + stripBounds.Y),
        };
    }

    private void ApplyDisplayedImage(ImageLoaded displayed)
    {
        _imagePanel.SetImage(displayed.Representation, displayed.IsPreview);
    }

    private sealed class ActivePaneIndicator : UiElement
    {
        internal override bool IsHitTestVisible => false;

        protected override void DrawCore(in UiDrawContext context)
        {
            context.DrawRoundedRectangle(
                new RoundedRectangle(
                    new RectangleF(PointF.Empty, Bounds.Size),
                    UiDesign.ControlCornerRadius,
                    UiDesign.ControlCornerRadius),
                context.Palette.Accent,
                strokeWidthPixels: 2.0f);
        }
    }
}
