using System.Diagnostics;
using System.Drawing;
using Dameview.Commands;
using Dameview.Diagnostics;
using Dameview.Notifications;
using Dameview.Settings;
using Dameview.UI.Animation;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.UI.Panels;
using Dameview.UI.Presentation;
using Dameview.UI.Workspace;
using Dameview.Updates;
using Dameview.Viewing;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI;

internal sealed class ViewerUi : UiElement, IDisposable
{
    private ID2D1DeviceContext _deviceContext;
    private ID2D1SolidColorBrush _brush;
    private readonly UiTextLayoutCache _textLayouts;
    private readonly UiDrawTally _drawTally = new();
    private readonly WorkspaceView _workspaceView;
    private readonly TabPreview _tabPreview;
    private readonly WorkspaceDragOverlay _dragOverlay;
    private readonly WorkspaceDragController _dragController;
    private readonly PerformanceOverlay _performanceOverlay;
    private readonly Overlay _mainOverlay;
    private readonly SplitView _splitView;
    private readonly GalleryPanel _galleryPanel;
    private readonly SettingsPanel _settingsPanel;
    private readonly CommandPalettePanel _commandPalettePanel;
    private readonly ModalHost _modalHost;
    private readonly ToastHost _toastHost;
    private readonly PopupHost _popupHost;
    private readonly UiAnimationClock _animationClock;
    private readonly UiRoot _root;
    private readonly Action<ViewerPane> _selectPane;
    // A ConditionalWeakTable could tie these states to tab reachability, but explicit
    // disposal keeps this UI ownership visible and deterministic.
    private readonly Dictionary<ViewerTab, GalleryPanelState> _galleryStates = [];
    private ViewerPane _activePane;
    private ViewerPaneView _activePaneView;
    private bool _animationsEnabled = true;
    private bool _galleryEnabled = true;
    private bool _chromeVisible = true;

    internal ViewerUi(
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        ViewerWorkspace workspace,
        float dpi,
        UiTheme theme,
        IAppCommands commands,
        IThumbnailImageLoader thumbnailLoader,
        PerformanceMonitor performanceMonitor,
        ToastService toasts,
        TimeProvider? timeProvider = null)
    {
        _deviceContext = deviceContext;
        _brush = deviceContext.CreateSolidColorBrush(default(Color4));
        _textLayouts = new UiTextLayoutCache(directWriteFactory);
        _selectPane = commands.SelectPane;
        Palette = theme;
        _animationClock = new UiAnimationClock(timeProvider);
        _activePane = workspace.ActivePane;
        _tabPreview = new TabPreview(thumbnailLoader);
        _workspaceView = new WorkspaceView(
            workspace.Root,
            // Reads the field, so panes opened after a device switch use the live context.
            pane => new ViewerPaneView(
                _deviceContext,
                pane,
                commands,
                index => commands.SelectTab(pane, index),
                index => commands.CloseTab(pane, index),
                () => commands.DuplicateActiveTab(pane),
                () => commands.ExecuteCommand(ViewerCommandId.OpenFile),
                ShowSettings,
                ShowTabPreview,
                HandleTabDragPointer,
                timeProvider));
        _activePaneView = FindPaneView(_activePane)
            ?? throw new InvalidOperationException("The active pane view was not created.");
        _workspaceView.SetActivePane(_activePane);
        _dragOverlay = new WorkspaceDragOverlay(thumbnailLoader);
        _dragController = new WorkspaceDragController(this, _workspaceView, _dragOverlay, commands);
        _performanceOverlay = new PerformanceOverlay(performanceMonitor)
        {
            IsVisible = false,
        };
        _galleryPanel = new GalleryPanel(
            deviceContext,
            thumbnailLoader,
            commands.SelectImage,
            commands.OpenImageInNewTab,
            HandleGalleryDragPointer);
        _galleryPanel.Bind(GetGalleryState(_activePane.ActiveTab));
        _mainOverlay = new Overlay(_workspaceView);
        _splitView = new SplitView(
            _mainOverlay,
            _galleryPanel,
            initialDividerOffsetDips: GalleryPanel.DefaultSizeDips);
        _splitView.ResizeStarted += _galleryPanel.BeginLiveResize;
        _splitView.ResizeCompleted += _galleryPanel.EndLiveResize;
        _splitView.ResizeCompleted += () => commands.UpdateSettings(
            settings => settings with { GallerySizeDips = _splitView.DividerOffsetDips });
        _modalHost = new ModalHost();
        _toastHost = new ToastHost(toasts);
        _popupHost = new PopupHost();
        _settingsPanel = new SettingsPanel(
            _popupHost,
            CloseModal,
            commands);
        _commandPalettePanel = new CommandPalettePanel(
            ViewerCommandCatalog.Commands,
            ViewerKeyBindings.Defaults,
            command =>
            {
                CloseModal();
                commands.ExecuteCommand(command);
            },
            keyBindings => commands.UpdateSettings(settings => settings with { KeyBindings = keyBindings }));

        AddChild(_splitView);
        AddChild(_tabPreview);
        AddChild(_dragOverlay);
        AddChild(_modalHost);
        AddChild(_popupHost);
        AddChild(_performanceOverlay);
        AddChild(_toastHost);
        _root = new UiRoot(this, dpi, _textLayouts);
        _root.CursorChanged += cursor => _cursorChanged?.Invoke(cursor);
        _root.PointerPressed += HandlePointerPressed;

        ApplyActivePaneState(_activePane.ActiveSession.State);
    }

    internal event Action? Invalidated
    {
        add => _root.Invalidated += value;
        remove => _root.Invalidated -= value;
    }

    private Action<WindowCursor>? _cursorChanged;

    internal event Action<WindowCursor>? CursorChanged
    {
        add => _cursorChanged += value;
        remove => _cursorChanged -= value;
    }

    internal UiTheme Palette { get; set; }
    internal TimeSpan? NextAnimationFrameDelay =>
        _workspaceView.NextAnimationFrameDelay
        ?? (_performanceOverlay.IsVisible ? PerformanceOverlay.HeartbeatInterval : null);
    internal bool IsClosingPane => _workspaceView.IsClosingPane;
    internal PaneLayoutArea PaneLayoutArea => new(
        MathF.Max(1.0f, _root.DipsToPixels(_workspaceView.Bounds.Width)),
        MathF.Max(1.0f, _root.DipsToPixels(_workspaceView.Bounds.Height)),
        _root.DipsToPixels(SplitPanel.SplitterSizeDips),
        _chromeVisible ? _root.DipsToPixels(ViewerTabStrip.HeightDips) : 0.0f,
        _root.DipsToPixels(SplitPanel.MinimumPaneSizeDips));

    internal PointF GetImageViewportPoint(PointF nativePoint)
    {
        PointF point = new(
            UiDpi.PixelsToDips(nativePoint.X, _root.Dpi),
            UiDpi.PixelsToDips(nativePoint.Y, _root.Dpi));
        RectangleF paneBounds = _activePaneView.GetBoundsRelativeTo(this);
        return _activePaneView.GetImageViewportPoint(
            new PointF(point.X - paneBounds.X, point.Y - paneBounds.Y),
            _root.Dpi);
    }

    internal void BindActivePane(ViewerPane pane)
    {
        _root.ClearPointer();
        _activePane = pane;
        if (FindPaneView(pane) is { } paneView)
        {
            _activePaneView = paneView;
        }

        _workspaceView.SetActivePane(pane);

        ViewerSessionState state = pane.ActiveSession.State;
        _galleryPanel.Bind(GetGalleryState(pane.ActiveTab));
        ApplyActivePaneState(state);
    }

    internal void ApplyLayout(WorkspaceNode root, WorkspaceSplit? openingSplit)
    {
        _root.ClearPointer();
        _tabPreview.Hide();
        _workspaceView.ApplyLayout(root, openingSplit);
        foreach (ViewerPaneView paneView in _workspaceView.PaneViews)
        {
            paneView.SetChromeVisible(_chromeVisible);
        }

        _activePaneView = FindPaneView(_activePane)
            ?? throw new InvalidOperationException("The active pane view is not attached.");
        _workspaceView.SetActivePane(_activePane);
    }

    internal void ApplyPaneRatios(WorkspaceNode root)
    {
        _root.ClearPointer();
        _tabPreview.Hide();
        _workspaceView.ApplyPaneRatios(root);
    }

    internal bool BeginClosePane(ViewerPane pane, Action completed)
    {
        _root.ClearPointer();
        _tabPreview.Hide();
        return _workspaceView.BeginClosePane(pane, completed);
    }

    internal void BindTab(ViewerPane pane, ViewerTab tab)
    {
        _root.ClearPointer();
        FindPaneView(pane)?.BindTab(tab);
        if (ReferenceEquals(pane, _activePane))
        {
            _galleryPanel.Bind(GetGalleryState(tab));
        }
    }

    internal void ApplyState(ViewerPane pane, ViewerSessionState state)
    {
        ViewerPaneView? paneView = FindPaneView(pane);
        bool isActivePane = ReferenceEquals(pane, _activePane);
        paneView?.ApplyState(state, clearPointer: isActivePane);
        if (isActivePane)
        {
            ApplyActivePaneState(state);
        }

        _root.InvalidateVisual();
    }

    internal void RecreateDeviceResources(ID2D1DeviceContext deviceContext)
    {
        _deviceContext = deviceContext;
        _brush.Dispose();
        _brush = deviceContext.CreateSolidColorBrush(default(Color4));
        _galleryPanel.RecreateDeviceResources(deviceContext);
        foreach (ViewerPaneView paneView in _workspaceView.PaneViews)
        {
            paneView.RecreateDeviceResources(deviceContext);
        }

        _root.InvalidateVisual();
    }

    internal void ApplyKeyBindings(ViewerKeyBindings keyBindings) =>
        _commandPalettePanel.ApplyKeyBindings(keyBindings);

    internal void ApplySettings(AppSettings settings)
    {
        _galleryEnabled = settings.GalleryEnabled;
        _splitView.SetEdge(GetGalleryEdge(settings.GalleryPlacement));
        _galleryPanel.SetOrientation(_splitView.IsHorizontal
            ? UiOrientation.Vertical
            : UiOrientation.Horizontal);
        _splitView.SetDividerOffset(settings.GallerySizeDips);
        _galleryPanel.SetThumbnailSize(settings.GalleryThumbnailSize);
        _settingsPanel.ApplySettings(settings);
        ApplyActivePaneState(_activePane.ActiveSession.State);
        if (_animationsEnabled == settings.AnimationsEnabled)
        {
            return;
        }

        _animationsEnabled = settings.AnimationsEnabled;
        _animationClock.Reset();
        _root.InvalidateVisual();
    }

    internal void ApplyUpdateState(UpdateState state) => _settingsPanel.ApplyUpdateState(state);

    internal void SetFullscreen(bool fullscreen)
    {
        _chromeVisible = !fullscreen;
        foreach (ViewerPaneView paneView in _workspaceView.PaneViews)
        {
            paneView.SetChromeVisible(_chromeVisible);
        }

        ApplyActivePaneState(_activePane.ActiveSession.State);
    }

    internal void ApplyTabs(ViewerPane pane, IReadOnlyList<ViewerTabInfo> tabs, int selectedIndex)
    {
        FindPaneView(pane)?.ApplyTabs(tabs, selectedIndex);
    }

    internal void CenterGallerySelection() => _galleryPanel.CenterSelection();

    internal bool TogglePerformanceOverlay()
    {
        _performanceOverlay.IsVisible = !_performanceOverlay.IsVisible;
        _root.InvalidateVisual();
        return _performanceOverlay.IsVisible;
    }

    internal bool IsCapturingShortcut => _commandPalettePanel.IsCapturing;

    internal bool HandleKey(WindowKeyEvent input)
    {
        if (_commandPalettePanel.HandleCaptureKey(input))
        {
            return true;
        }

        if (input.Key == WindowKey.Escape && _dragController.IsActive)
        {
            _root.CancelPointer();
            return true;
        }

        if (_popupHost.IsOpen)
        {
            if (input.Key == WindowKey.Escape)
            {
                return _popupHost.HandleEscape();
            }

            if (input.Key == WindowKey.Tab)
            {
                _popupHost.Close();
            }
        }

        if (_modalHost.IsOpen)
        {
            if (input.Key == WindowKey.Escape)
            {
                return _modalHost.HandleEscape();
            }

            _root.HandleKey(
                input,
                _modalHost.Content!,
                wrapFocus: true,
                directionalNavigation: true);
            return true;
        }

        return _root.HandleKey(
            input,
            _activePaneView.FocusScope,
            wrapFocus: false,
            directionalNavigation: false);
    }

    internal bool HandleTextInput(string text) => _root.HandleTextInput(text);

    internal void SetDpi(float dpi) => _root.SetDpi(dpi);

    internal bool Update()
    {
        UiUpdateContext context = _animationClock.GetNextFrame(_animationsEnabled);
        bool continues = _root.Update(context);
        _workspaceView.CompletePendingClose();
        // Only real animation work counts: a heartbeat that merely keeps the overlay ticking
        // must not hold the clock open, or the next animation starts with a frame's backlog.
        if (!continues && _workspaceView.NextAnimationFrameDelay is null)
        {
            _animationClock.Reset();
        }

        return continues;
    }

    /// <summary>What the last frame spent laying the tree out, for the performance overlay.</summary>
    internal TimeSpan LastLayoutTime { get; private set; }
    /// <summary>What the last frame spent walking the tree and drawing it.</summary>
    /// <remarks>
    /// Wall time, not the cost of the drawing calls themselves: Direct2D processes a batch
    /// whenever its internal buffer fills, so work belonging to one call can be billed to a
    /// later one, and some of what a frame submits is paid here rather than in EndDraw.
    /// </remarks>
    internal TimeSpan LastDrawTime { get; private set; }
    internal int LayoutPasses => _root.LayoutPasses;
    internal int LastDrawnElements => _drawTally.Elements;
    internal int LastDrawOperations => _drawTally.Operations;

    internal void DrawFrame(SizeF pixelSize)
    {
        _workspaceView.UpdateStatuses();

        long layoutStarted = Stopwatch.GetTimestamp();
        _root.Arrange(pixelSize);
        LastLayoutTime = Stopwatch.GetElapsedTime(layoutStarted);

        // The tree is arranged by now, so this only walks and draws it.
        _drawTally.Reset();
        var context = new UiDrawContext(
            _deviceContext, _brush, _textLayouts, _drawTally, Palette, _root.Dpi);
        long drawStarted = Stopwatch.GetTimestamp();
        _root.Draw(context, pixelSize);
        LastDrawTime = Stopwatch.GetElapsedTime(drawStarted);
    }

    internal bool HandlePointer(in WindowPointerEvent input) => _root.HandlePointer(input);

    internal void HandleFileDrag(in WindowFileDragEvent input)
    {
        WorkspaceDragEventKind kind = input.Kind switch
        {
            WindowFileDragKind.Entered => WorkspaceDragEventKind.Started,
            WindowFileDragKind.Moved => WorkspaceDragEventKind.Moved,
            WindowFileDragKind.Dropped => WorkspaceDragEventKind.Completed,
            WindowFileDragKind.Left => WorkspaceDragEventKind.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };
        var position = new PointF(
            UiDpi.PixelsToDips(input.Position.X, _root.Dpi),
            UiDpi.PixelsToDips(input.Position.Y, _root.Dpi));
        _dragController.HandleExternalFiles(input.Paths, new WorkspaceDragEvent(kind, position));
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        _splitView.Measure(availableSize);
        _tabPreview.Measure(availableSize);
        _dragOverlay.Measure(availableSize);
        _modalHost.Measure(availableSize);
        _popupHost.Measure(availableSize);
        _performanceOverlay.Measure(availableSize);
        _toastHost.Measure(availableSize);
        return availableSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        _splitView.Arrange(new RectangleF(PointF.Empty, finalSize));
        RectangleF paneBounds = _activePaneView.GetBoundsRelativeTo(_mainOverlay);
        RectangleF contentBounds = _activePaneView.ContentBounds;
        contentBounds.Offset(paneBounds.Location);
        _tabPreview.Arrange(new RectangleF(PointF.Empty, finalSize));
        _dragOverlay.Arrange(new RectangleF(PointF.Empty, finalSize));
        _modalHost.Arrange(new RectangleF(PointF.Empty, finalSize));
        _popupHost.Arrange(new RectangleF(PointF.Empty, finalSize));
        _toastHost.Arrange(new RectangleF(PointF.Empty, finalSize));
        SizeF performanceSize = _performanceOverlay.DesiredSize;
        _performanceOverlay.Arrange(new RectangleF(
            UiDesign.WindowMargin,
            UiDesign.WindowMargin,
            performanceSize.Width,
            performanceSize.Height));
    }

    public void Dispose()
    {
        _root.ClearPointer();
        _root.SetFocus(null);
        _modalHost.Close();
        _popupHost.Close();
        _dragController.Cancel();
        foreach (ViewerTab tab in _galleryStates.Keys)
        {
            tab.Disposed -= HandleTabDisposed;
        }

        _galleryStates.Clear();
        _dragOverlay.Dispose();
        _toastHost.Dispose();
        _tabPreview.Dispose();
        _galleryPanel.Dispose();
        _workspaceView.Dispose();
        _textLayouts.Dispose();
        _brush.Dispose();
    }

    private void ShowTabPreview(ViewerPane pane, ViewerTabInfo? tab, RectangleF tabBounds)
    {
        if (tab is not { ImagePath: string path })
        {
            _tabPreview.Hide();
            return;
        }

        ViewerPaneView paneView = FindPaneView(pane)
            ?? throw new InvalidOperationException("The pane view is not attached.");
        RectangleF paneBounds = paneView.GetBoundsRelativeTo(this);
        tabBounds.Offset(paneBounds.Location);
        _tabPreview.Show(path, tabBounds);
    }

    private void HandleTabDragPointer(
        ViewerPane pane,
        int tabIndex,
        WorkspaceDragEvent input)
    {
        if (input.Kind == WorkspaceDragEventKind.Started)
        {
            _tabPreview.Hide();
        }

        _dragController.HandleTabPointer(pane, tabIndex, input);
    }

    private void HandleGalleryDragPointer(string path, WorkspaceDragEvent input)
    {
        if (input.Kind == WorkspaceDragEventKind.Started)
        {
            _tabPreview.Hide();
        }

        _dragController.HandleGalleryPointer(_galleryPanel, path, input);
    }

    private void HandlePointerPressed(UiElement? target)
    {
        for (UiElement? element = target; element is not null; element = element.Parent)
        {
            if (element is ViewerPaneView paneView)
            {
                _selectPane(paneView.Pane);
                return;
            }
        }
    }

    internal void ShowSettings()
    {
        ShowModal(_settingsPanel, CloseModal);
    }

    internal void ShowCommandPalette()
    {
        _commandPalettePanel.Reset();
        ShowModal(_commandPalettePanel, CloseModal);
    }

    private void ShowModal(ModalContent content, Action dismiss)
    {
        _root.ClearPointer();
        _root.SetFocus(null);
        _popupHost.Close();
        _modalHost.Show(content, dismiss);
        _root.SetFocus(content.InitialFocus);
    }

    private void CloseModal()
    {
        if (!_modalHost.IsOpen)
        {
            return;
        }

        _root.ClearPointer();
        _root.SetFocus(null);
        _popupHost.Close();
        _modalHost.Close();
    }

    private void ApplyActivePaneState(ViewerSessionState state)
    {
        _splitView.SecondPaneVisible = _chromeVisible && _galleryEnabled && ShouldShowGallery(state);
        _galleryPanel.ApplyState(state.FolderEntries, state.RequestedPath);
    }

    private static bool ShouldShowGallery(ViewerSessionState state)
    {
        return state.RequestedPath is not null
            && !state.IsError
            && state.FolderError is null;
    }

    private static SplitViewEdge GetGalleryEdge(GalleryPlacement placement) => placement switch
    {
        GalleryPlacement.Right => SplitViewEdge.Right,
        GalleryPlacement.Left => SplitViewEdge.Left,
        GalleryPlacement.Top => SplitViewEdge.Top,
        GalleryPlacement.Bottom => SplitViewEdge.Bottom,
        _ => throw new ArgumentOutOfRangeException(nameof(placement), placement, null),
    };

    private ViewerPaneView? FindPaneView(ViewerPane pane)
    {
        return _workspaceView.FindPaneView(pane);
    }

    private GalleryPanelState GetGalleryState(ViewerTab tab)
    {
        if (!_galleryStates.TryGetValue(tab, out GalleryPanelState? state))
        {
            state = new GalleryPanelState();
            _galleryStates.Add(tab, state);
            tab.Disposed += HandleTabDisposed;
        }

        return state;
    }

    private void HandleTabDisposed(ViewerTab tab)
    {
        tab.Disposed -= HandleTabDisposed;
        _galleryStates.Remove(tab);
    }
}

internal readonly record struct ViewerTabInfo(string Label, string? ImagePath);
