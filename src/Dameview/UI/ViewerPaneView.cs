using System.Drawing;
using Dameview.Imaging;
using Dameview.Platform;
using Dameview.UI.Components;
using Dameview.UI.Layout;
using Dameview.UI.Panels;
using Dameview.Viewing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Dameview.UI;

// Presents the active tab of one viewer pane. Shared application chrome remains in ViewerUi.
internal sealed class ViewerPaneView : UiElement, IDisposable
{
    private readonly ImagePanel _imagePanel;
    private readonly ViewerTabStrip _viewerTabs;
    private readonly EmptyStatePanel _emptyStatePanel;
    private readonly Overlay _contentOverlay;
    private readonly StatusPanel _statusPanel;
    private readonly ActivePaneIndicator _activePaneIndicator;
    private readonly Action<ViewerPane, ViewerTabInfo?, RectangleF> _hoveredTabChanged;
    private ViewerSessionState _state;

    internal ViewerPaneView(
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        ViewerPane pane,
        Action<int> selectTab,
        Action<int> closeTab,
        Action addTab,
        Action showSettings,
        Action<ViewerPane, ViewerTabInfo?, RectangleF> hoveredTabChanged,
        Action<ViewerPane, int, WorkspaceDragEvent> tabDragPointer,
        TimeProvider? timeProvider = null,
        UiPost? postToUi = null)
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
            timeProvider,
            postToUi);
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
            LoadApplicationIcon(deviceContext),
            showSettings);
        _contentOverlay = new Overlay(_imagePanel, _emptyStatePanel);
        _statusPanel = new StatusPanel(directWriteFactory);
        _activePaneIndicator = new ActivePaneIndicator { IsVisible = false };

        AddChild(_viewerTabs);
        AddChild(_contentOverlay);
        AddChild(_statusPanel);
        AddChild(_activePaneIndicator);

        bool hasImage = HasImage;
        _imagePanel.IsVisible = hasImage;
        _emptyStatePanel.IsVisible = !hasImage;
        _statusPanel.IsVisible = HasStatus;
        if (_state.DisplayedImage is { } displayed)
        {
            ApplyDisplayedImage(displayed);
        }
    }

    internal ViewerPane Pane { get; }
    internal bool HasImage => _state.DisplayedImage is not null;
    internal bool HasStatus => SettingsError is not null
        || HasImage
        || _state.Message is not null
        || _state.FolderError is not null;
    internal RectangleF ContentBounds { get; private set; }
    internal UiElement EmptyStateFocusScope => _emptyStatePanel;
    internal UiElement EmptyStateSettingsButton => _emptyStatePanel.SettingsButton;
    internal TimeSpan? NextAnimationFrameDelay => _imagePanel.NextAnimationFrameDelay;
    internal RectangleF TabStripBounds => _viewerTabs.GetBoundsRelativeTo(this);

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
            _statusPanel.IsVisible = HasStatus;
            InvalidateVisual();
        }
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

        bool hasImage = HasImage;
        _imagePanel.IsVisible = hasImage;
        _emptyStatePanel.IsVisible = !hasImage;
        _statusPanel.IsVisible = HasStatus;
        InvalidateVisual();
    }

    internal void ApplyTabs(IReadOnlyList<ViewerTabInfo> tabs, int selectedIndex)
    {
        _viewerTabs.SetTabs(tabs, selectedIndex);
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
        string? message = SettingsError ?? animationError ?? _state.Message;
        if (message is null && _state.FolderError is { } folderError)
        {
            message = $"Image opened, but its folder could not be read: {folderError}";
        }

        _statusPanel.Status = new ViewerStatus(
            Path.GetFileName(_state.RequestedPath) ?? string.Empty,
            _state.DisplayedImage?.Representation.Width ?? 0,
            _state.DisplayedImage?.Representation.Height ?? 0,
            _imagePanel.ZoomPercentage,
            message,
            SettingsError is not null || animationError is not null || _state.IsError || _state.FolderError is not null);
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float tabHeight = _viewerTabs.Measure(
            new SizeF(availableSize.Width, ViewerTabStrip.HeightDips)).Height;
        var contentSize = new SizeF(
            availableSize.Width,
            MathF.Max(0.0f, availableSize.Height - tabHeight));
        _contentOverlay.Measure(contentSize);
        if (_statusPanel.IsVisible)
        {
            _statusPanel.Measure(contentSize);
        }

        return availableSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        float tabHeight = ViewerTabStrip.HeightDips;
        _viewerTabs.Arrange(new RectangleF(0.0f, 0.0f, finalSize.Width, tabHeight));
        ContentBounds = new RectangleF(
            0.0f,
            tabHeight,
            finalSize.Width,
            MathF.Max(0.0f, finalSize.Height - tabHeight));
        _contentOverlay.Arrange(ContentBounds);

        RectangleF status = ViewerLayout.Calculate(
            ContentBounds.Size,
            showStatus: HasStatus,
            showToolbar: false).Status;
        status.Offset(ContentBounds.Location);
        _statusPanel.Arrange(status);
        _activePaneIndicator.Arrange(new RectangleF(PointF.Empty, finalSize));
    }

    protected override bool HitTestCore(PointF position) => false;

    public void Dispose()
    {
        _viewerTabs.Dispose();
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

    private static ID2D1Bitmap1 LoadApplicationIcon(ID2D1DeviceContext deviceContext)
    {
        using Stream stream = typeof(ViewerPaneView).Assembly.GetManifestResourceStream(
            "Dameview.Assets.dameview.png")
            ?? throw new InvalidOperationException("The embedded application icon could not be found.");
        using var decoder = new ImageDecoder();
        return D2DBitmapFactory.Create(deviceContext, decoder.Decode(stream));
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
                strokeWidth: 2.0f);
        }
    }
}
