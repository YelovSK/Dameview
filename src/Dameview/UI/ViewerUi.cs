using System.Diagnostics;
using System.Drawing;
using Dameview.Commands;
using Dameview.Diagnostics;
using Dameview.Notifications;
using Dameview.Settings;
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

namespace Dameview.UI;

internal sealed class ViewerUi : UiElement, IDisposable
{
    private readonly UiHost _host;
    private readonly WorkspaceView _workspaceView;
    private readonly TabPreview _tabPreview;
    private readonly ToolTipHost _toolTipHost = new();
    private readonly WorkspaceDragOverlay _dragOverlay;
    private readonly WorkspaceDragController _dragController;
    private readonly PerformanceOverlay _performanceOverlay;
    private readonly SplitView _splitView;
    private readonly GalleryPanel _galleryPanel;
    private readonly SettingsPanel _settingsPanel;
    private readonly CommandPalettePanel _commandPalettePanel;
    private readonly ModalHost _modalHost;
    private readonly ToastHost _toastHost;
    private readonly PopupHost _popupHost;
    private readonly ViewerContextMenus _contextMenus;
    private readonly Action<ViewerPane> _selectPane;
    // A ConditionalWeakTable could tie these states to tab reachability, but explicit
    // disposal keeps this UI ownership visible and deterministic.
    private readonly Dictionary<ViewerTab, GalleryPanelState> _galleryStates = [];
    private ViewerPane _activePane;
    private ViewerPaneView _activePaneView;
    private bool _animationsEnabled = true;
    private bool _galleryEnabled = true;
    private bool _chromeVisible = true;
    // Read when a pane view is created, so panes opened later show the current bindings.
    private ViewerKeyBindings _keyBindings = ViewerKeyBindings.Defaults;

    internal ViewerUi(
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        ViewerWorkspace workspace,
        float dpi,
        UiTheme theme,
        IAppActions app,
        IThumbnailImageLoader thumbnailLoader,
        PerformanceMonitor performanceMonitor,
        ToastService toasts,
        TimeProvider? timeProvider = null)
    {
        _host = new UiHost(this, deviceContext, directWriteFactory, dpi, theme, timeProvider);
        _selectPane = app.SelectPane;
        _activePane = workspace.ActivePane;
        _tabPreview = new TabPreview(thumbnailLoader);
        _popupHost = new PopupHost();
        _contextMenus = new ViewerContextMenus(_popupHost, app);
        _workspaceView = new WorkspaceView(
            workspace.Root,
            // Reads the host, so panes opened after a device switch use the live context.
            pane => new ViewerPaneView(
                _host.DeviceContext,
                pane,
                app,
                _contextMenus,
                index => app.SelectTab(pane, index),
                ShowTabPreview,
                HandleTabDragPointer,
                _keyBindings));
        _activePaneView = FindPaneView(_activePane)
            ?? throw new InvalidOperationException("The active pane view was not created.");
        _workspaceView.SetActivePane(_activePane);
        _dragOverlay = new WorkspaceDragOverlay(thumbnailLoader);
        _dragController = new WorkspaceDragController(this, _workspaceView, _dragOverlay, app);
        _performanceOverlay = new PerformanceOverlay(performanceMonitor)
        {
            IsVisible = false,
        };
        _galleryPanel = new GalleryPanel(
            deviceContext,
            thumbnailLoader,
            app.SelectImage,
            app.OpenImageInNewTab,
            () => app.Execute(AppCommands.ToggleFlattenFolder, app.ActiveContext),
            HandleGalleryDragPointer,
            (path, point) => _contextMenus.ShowForGalleryItem(path, _galleryPanel!, point));
        _galleryPanel.Bind(GetGalleryState(_activePane.ActiveTab));
        _splitView = new SplitView(
            _workspaceView,
            _galleryPanel,
            initialDividerOffsetDips: GalleryPanel.DefaultSizeDips);
        _splitView.ResizeCompleted += () => app.UpdateSettings(
            settings => settings with { GallerySizeDips = _splitView.DividerOffsetDips });
        _modalHost = new ModalHost();
        _toastHost = new ToastHost(toasts);
        _settingsPanel = new SettingsPanel(
            _popupHost,
            CloseModal,
            app);
        _commandPalettePanel = new CommandPalettePanel(
            AppCommands.All,
            ViewerKeyBindings.Defaults,
            command =>
            {
                CloseModal();
                app.Execute(command, app.ActiveContext);
            },
            command => app.CanExecute(command, app.ActiveContext),
            keyBindings => app.UpdateSettings(settings => settings with { KeyBindings = keyBindings }));

        AddChild(_splitView);
        AddChild(_tabPreview);
        AddChild(_dragOverlay);
        AddChild(_modalHost);
        AddChild(_popupHost);
        AddChild(_performanceOverlay);
        AddChild(_toastHost);
        AddChild(_toolTipHost);
        _host.Root.CursorChanged += cursor => _cursorChanged?.Invoke(cursor);
        _host.Root.PointerPressed += HandlePointerPressed;
        _host.Root.PointerPressed += _ => _toolTipHost.Hide();
        _host.Root.ToolTipTargetChanged += _toolTipHost.Show;

        ApplyActivePaneState(_activePane.ActiveSession.State);
    }

    internal event Action? Invalidated
    {
        add => _host.Root.Invalidated += value;
        remove => _host.Root.Invalidated -= value;
    }

    private Action<WindowCursor>? _cursorChanged;

    internal event Action<WindowCursor>? CursorChanged
    {
        add => _cursorChanged += value;
        remove => _cursorChanged -= value;
    }

    internal UiTheme Palette
    {
        get => _host.Palette;
        set => _host.Palette = value;
    }
    internal TimeSpan? NextAnimationFrameDelay =>
        _workspaceView.NextAnimationFrameDelay
        ?? (_performanceOverlay.IsVisible ? PerformanceOverlay.HeartbeatInterval : null);
    internal bool IsClosingPane => _workspaceView.IsClosingPane;
    internal PaneLayoutArea PaneLayoutArea => new(
        MathF.Max(1.0f, _host.Root.DipsToPixels(_workspaceView.Bounds.Width)),
        MathF.Max(1.0f, _host.Root.DipsToPixels(_workspaceView.Bounds.Height)),
        _host.Root.DipsToPixels(SplitPanel.SplitterSizeDips),
        _chromeVisible ? _host.Root.DipsToPixels(ViewerPaneView.TabRowHeightDips) : 0.0f,
        _host.Root.DipsToPixels(SplitPanel.MinimumPaneSizeDips));

    internal PointF GetImageViewportPoint(PointF nativePoint)
    {
        PointF point = new(
            UiDpi.PixelsToDips(nativePoint.X, _host.Root.Dpi),
            UiDpi.PixelsToDips(nativePoint.Y, _host.Root.Dpi));
        RectangleF paneBounds = _activePaneView.GetBoundsRelativeTo(this);
        return _activePaneView.GetImageViewportPoint(
            new PointF(point.X - paneBounds.X, point.Y - paneBounds.Y),
            _host.Root.Dpi);
    }

    internal void BindActivePane(ViewerPane pane)
    {
        _host.Root.ClearPointer();
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
        _host.Root.ClearPointer();
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
        _host.Root.ClearPointer();
        _tabPreview.Hide();
        _workspaceView.ApplyPaneRatios(root);
    }

    internal bool BeginClosePane(ViewerPane pane, Action completed)
    {
        _host.Root.ClearPointer();
        _tabPreview.Hide();
        return _workspaceView.BeginClosePane(pane, completed);
    }

    internal void BindTab(ViewerPane pane, ViewerTab tab)
    {
        _host.Root.ClearPointer();
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

        _host.Root.InvalidateVisual();
    }

    internal void RecreateDeviceResources(ID2D1DeviceContext deviceContext)
    {
        _host.RecreateDeviceResources(deviceContext);
        _galleryPanel.RecreateDeviceResources(deviceContext);
        foreach (ViewerPaneView paneView in _workspaceView.PaneViews)
        {
            paneView.RecreateDeviceResources(deviceContext);
        }
    }

    internal void ApplyKeyBindings(ViewerKeyBindings keyBindings)
    {
        _commandPalettePanel.ApplyKeyBindings(keyBindings);
        _contextMenus.KeyBindings = keyBindings;
        _keyBindings = keyBindings;
        foreach (ViewerPaneView paneView in _workspaceView.PaneViews)
        {
            paneView.ApplyKeyBindings(keyBindings);
        }
    }

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
        _host.ResetClock();
        _host.Root.InvalidateVisual();
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
        _host.Root.InvalidateVisual();
        return _performanceOverlay.IsVisible;
    }

    internal bool HandleCapturedKey(WindowKeyEvent input) => _host.Root.HandleCapturedKey(input);

    internal bool HandleKey(WindowKeyEvent input)
    {
        if (input.Key == WindowKey.Escape && _dragController.IsActive)
        {
            _host.Root.CancelPointer();
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

            _host.Root.HandleKey(
                input,
                _modalHost.Content!,
                wrapFocus: true,
                directionalNavigation: true);
            return true;
        }

        return _host.Root.HandleKey(
            input,
            _activePaneView.FocusScope,
            wrapFocus: false,
            directionalNavigation: false);
    }

    internal bool HandleTextInput(string text) => _host.Root.HandleTextInput(text);

    internal void SetDpi(float dpi) => _host.Root.SetDpi(dpi);

    internal void BeginResize() => _host.Root.BeginResize();

    internal void EndResize() => _host.Root.EndResize();

    internal bool Update()
    {
        bool continues = _host.Update(_animationsEnabled);
        _workspaceView.CompletePendingClose();
        // Only real animation work counts: a heartbeat that merely keeps the overlay ticking
        // must not hold the clock open, or the next animation starts with a frame's backlog.
        if (!continues && _workspaceView.NextAnimationFrameDelay is null)
        {
            _host.ResetClock();
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
    internal int LayoutPasses => _host.Root.LayoutPasses;
    internal int LastDrawnElements => _host.LastDrawnElements;
    internal int LastDrawOperations => _host.LastDrawOperations;

    internal void DrawFrame(SizeF pixelSize)
    {
        _workspaceView.UpdateStatuses();

        long layoutStarted = Stopwatch.GetTimestamp();
        _host.Root.Arrange(pixelSize);
        LastLayoutTime = Stopwatch.GetElapsedTime(layoutStarted);

        // The tree is arranged by now, so this only walks and draws it.
        long drawStarted = Stopwatch.GetTimestamp();
        _host.Draw(pixelSize);
        LastDrawTime = Stopwatch.GetElapsedTime(drawStarted);
    }

    internal bool HandlePointer(in WindowPointerEvent input) => _host.Root.HandlePointer(input);

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
            UiDpi.PixelsToDips(input.Position.X, _host.Root.Dpi),
            UiDpi.PixelsToDips(input.Position.Y, _host.Root.Dpi));
        _dragController.HandleExternalFiles(input.Paths, new WorkspaceDragEvent(kind, position));
    }

    // Every child is a layer covering the whole window, stacked in the order they were added.
    public void Dispose()
    {
        _host.Dispose();
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
        _host.Root.ClearPointer();
        _host.Root.SetFocus(null);
        _popupHost.Close();
        _modalHost.Show(content, dismiss);
        _host.Root.SetFocus(content.InitialFocus);
    }

    private void CloseModal()
    {
        if (!_modalHost.IsOpen)
        {
            return;
        }

        _host.Root.ClearPointer();
        _host.Root.SetFocus(null);
        _popupHost.Close();
        _modalHost.Close();
    }

    private void ApplyActivePaneState(ViewerSessionState state)
    {
        _splitView.SecondPaneVisible = _chromeVisible && _galleryEnabled && ShouldShowGallery(state);
        _galleryPanel.ApplyState(state);
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
