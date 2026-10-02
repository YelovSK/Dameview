using System.Drawing;
using Dameview.Commands;
using Dameview.Imaging.Loading;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.UI.Panels;
using Dameview.Viewing;
using Dameview.Win32.Input;
using Vortice.Direct2D1;

namespace Dameview.UI.Workspace;

// Presents the active tab of one viewer pane. Shared application chrome remains in ViewerUi.
internal sealed class ViewerPaneView : UiElement, IDisposable
{
    private static readonly UiThickness TabStripMargin = new(UiDesign.Spacing, UiDesign.SmallSpacing);

    /// <summary>The height the tab strip takes from the top of a pane while it is shown.</summary>
    internal const float TabRowHeightDips = ViewerTabStrip.HeightDips + (2.0f * UiDesign.SmallSpacing);

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
    private bool _pointerNearToolbar;
    private bool _pointerNearStatus;

    internal ViewerPaneView(
        ID2D1DeviceContext deviceContext,
        ViewerPane pane,
        ICommandRunner commands,
        ViewerContextMenus contextMenus,
        Action<int> selectTab,
        Action<ViewerPane, ViewerTabInfo?, RectangleF> hoveredTabChanged,
        Action<ViewerPane, int, WorkspaceDragEvent> tabDragPointer,
        ViewerKeyBindings keyBindings)
    {
        void Run(Command command, ViewerTab target) => commands.Execute(command, CommandContext.For(target));

        Pane = pane;
        _hoveredTabChanged = hoveredTabChanged;
        ViewerTab tab = pane.ActiveTab;
        ViewerSession session = tab.Session;
        _state = session.State;
        _imagePanel = new ImagePanel(
            deviceContext,
            session.Viewport,
            session.Animator,
            point => contextMenus.ShowForImage(Pane.ActiveTab, _imagePanel!, point));
        _viewerTabs = new ViewerTabStrip(
            [new ViewerTabInfo("Dameview", null)],
            0,
            selectTab,
            index => Run(AppCommands.CloseTab, pane.Tabs[index]),
            () => Run(AppCommands.NewTab, pane.ActiveTab),
            HandleHoveredTabChanged,
            (index, input) => tabDragPointer(Pane, index, TranslateTabStripEvent(input)),
            (index, point) => contextMenus.ShowForTab(Pane.Tabs[index], _viewerTabs!, point))
        {
            Margin = TabStripMargin,
        };
        _emptyStatePanel = new EmptyStatePanel(deviceContext, commands, pane, keyBindings);
        _toolbarPanel = new ToolbarPanel(commands, pane)
        {
            HorizontalAlignment = UiAlignment.Center,
            VerticalAlignment = UiAlignment.Start,
            Margin = new UiThickness(UiDesign.WindowMargin),
        };
        _statusPanel = new StatusPanel
        {
            HorizontalAlignment = UiAlignment.Center,
            VerticalAlignment = UiAlignment.End,
            Margin = new UiThickness(UiDesign.WindowMargin),
        };
        _contentOverlay = new Overlay(_imagePanel, _emptyStatePanel, _toolbarPanel, _statusPanel);
        _activePaneIndicator = new ActivePaneIndicator { IsVisible = false };

        AddChild(new StackPanel(UiOrientation.Vertical, _viewerTabs, _contentOverlay)
        {
            Fill = _contentOverlay,
        });
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
    internal RectangleF ContentBounds => _contentOverlay.GetBoundsRelativeTo(this);
    internal UiElement FocusScope => HasImage ? _toolbarPanel : _emptyStatePanel;

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
    }

    internal void ApplyKeyBindings(ViewerKeyBindings keyBindings) =>
        _emptyStatePanel.ApplyKeyBindings(keyBindings);

    internal bool ShowActivePaneIndicator
    {
        get => _activePaneIndicator.IsVisible;
        set => _activePaneIndicator.IsVisible = value;
    }

    private void UpdateChromeVisibility()
    {
        bool hasImage = HasImage;
        _imagePanel.IsVisible = hasImage;
        _emptyStatePanel.IsVisible = !hasImage && !_state.IsLoading;
        // A single tab that holds nothing is not worth a strip to switch between.
        _viewerTabs.IsVisible = _chromeVisible && (Pane.Count > 1 || hasImage);
        _toolbarPanel.IsPresent = _chromeVisible && hasImage && _pointerNearToolbar;
        _statusPanel.IsPresent = _chromeVisible && HasStatus && (_pointerNearStatus || _statusPanel.HasMessage);
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
        if (displayedImageChanged && state.DisplayedImage is null)
        {
            _imagePanel.ClearImage();
        }
        else if (displayedImageChanged && state.DisplayedImage is { } displayed)
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

        SizeF imageSize = HasImage ? _imagePanel.ImageSize : SizeF.Empty;
        _statusPanel.SetStatus(new ViewerStatus(
            Path.GetFileName(_state.RequestedPath) ?? string.Empty,
            (int)imageSize.Width,
            (int)imageSize.Height,
            _state.CurrentEntry?.Length,
            _imagePanel.ZoomPercentage,
            message,
            animationError is not null || _state.IsError || _state.FolderError is not null));
        UpdateChromeVisibility();
    }

    protected override bool HitTestCore(PointF position) => false;

    protected override void ObservePointerMove(in WindowPointerEvent input)
    {
        bool insidePane = input.Position.X >= 0.0f
            && input.Position.X < Bounds.Width
            && input.Position.Y >= 0.0f
            && input.Position.Y < Bounds.Height;
        _pointerNearStatus = insidePane && input.Position.Y >= Bounds.Height - StatusPanel.HeightDips - 24.0f;
        _pointerNearToolbar = insidePane
            && input.Position.Y >= ContentBounds.Y
            && input.Position.Y <= ContentBounds.Y
                + UiDesign.WindowMargin
                + ToolbarPanel.HeightDips
                + 28.0f;
        UpdateChromeVisibility();
    }

    protected override void ObservePointerLeave()
    {
        _pointerNearStatus = false;
        _pointerNearToolbar = false;
        UpdateChromeVisibility();
    }

    internal void RecreateDeviceResources(ID2D1DeviceContext deviceContext)
    {
        _imagePanel.RecreateDeviceResources(deviceContext);
        _emptyStatePanel.RecreateDeviceResources(deviceContext);
    }

    public void Dispose()
    {
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
