using System.Diagnostics;
using System.Drawing;
using Dameview.Commands;
using Dameview.Diagnostics;
using Dameview.Imaging.Loading;
using Dameview.Notifications;
using Dameview.Settings;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.UI.Panels;
using Dameview.UI.Workspace;
using Dameview.Updates;
using Dameview.Viewing;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Dameview.UI;

internal sealed class ViewerUi : UiElement, IDisposable
{
    // The panes' tab rows make up the title bar, so it is as tall as one.
    internal const float TitleBarHeightDips = ViewerPaneView.TabRowHeightDips;
    // Sits where a tab would, so it lines up with the tabs beside it.
    private static readonly UiThickness MenuButtonMargin = ViewerPaneView.TabStripMargin with { Right = 0.0f, Bottom = 0.0f };
    private const float MenuButtonSizeDips = ViewerTabStrip.HeightDips;

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
    private readonly WindowButtons _windowButtons;
    private readonly Button _menuButton;
    private readonly ViewerContextMenus _contextMenus;
    private readonly ViewerWorkspace _workspace;
    private readonly IAppActions _app;
    private bool _galleryEnabled = true;
    private bool _chromeVisible = true;
    // Read when a pane view is created, so panes opened later show the current bindings.
    private ViewerKeyBindings _keyBindings = ViewerKeyBindings.Defaults;
    private bool _sharpPixels;
    // In DIPs; the window center stands in until the pointer first moves.
    private PointF? _pointer;

    internal ViewerUi(
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        ViewerWorkspace workspace,
        float dpi,
        UiTheme theme,
        IAppActions app,
        IImagePipeline images,
        PerformanceMonitor performanceMonitor,
        ToastService toasts,
        WindowButtons windowButtons,
        TimeProvider? timeProvider = null)
    {
        _host = new UiHost(this, deviceContext, directWriteFactory, dpi, theme, timeProvider);
        _workspace = workspace;
        _app = app;
        _tabPreview = new TabPreview(images);
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
                (tabPane, tabIndex, input) => _dragController!.HandleTabPointer(tabPane, tabIndex, input),
                _keyBindings)
            {
                SharpPixels = _sharpPixels,
                ChromeVisible = _chromeVisible,
            },
            _host.CreateSnapshot);
        _workspaceView.SetActivePane(workspace.ActivePane);
        _dragOverlay = new WorkspaceDragOverlay(images);
        _dragController = new WorkspaceDragController(this, _workspaceView, _dragOverlay, app);
        _performanceOverlay = new PerformanceOverlay(performanceMonitor)
        {
            IsVisible = false,
        };
        _galleryPanel = new GalleryPanel(
            deviceContext,
            images,
            app.SelectImage,
            app.OpenImageInNewTab,
            () => app.Execute(AppCommands.ToggleFlattenFolder, app.ActiveContext),
            button => _contextMenus.ShowSortMenu(workspace.ActiveTab, button),
            (path, input) => _dragController.HandleGalleryPointer(_galleryPanel!, path, input),
            (path, point) => _contextMenus.ShowForGalleryItem(path, _galleryPanel!, point));
        _splitView = new SplitView(
            _workspaceView,
            _galleryPanel,
            initialDividerOffsetDips: GalleryPanel.DefaultSizeDips);
        _splitView.ResizeCompleted += () => app.UpdateSettings(
            settings => settings with { GallerySizeDips = _splitView.DividerOffsetDips });
        _modalHost = new ModalHost();
        _windowButtons = windowButtons;
        _menuButton = new Button(
            UiTypography.MenuIcon,
            () => _contextMenus.ShowAppMenu(_menuButton!),
            fontFamily: UiTypography.IconFontFamily,
            fontSize: 16.0f,
            filled: false)
        {
            ToolTip = new("Menu"),
            HorizontalAlignment = UiAlignment.Start,
            VerticalAlignment = UiAlignment.Start,
            Margin = MenuButtonMargin,
            MaxWidth = MenuButtonSizeDips,
            MaxHeight = MenuButtonSizeDips,
        };
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
        AddChild(_menuButton);
        AddChild(_tabPreview);
        AddChild(_dragOverlay);
        AddChild(_modalHost);
        // Above any modal, so the window can always be minimized or closed.
        AddChild(_windowButtons);
        AddChild(_popupHost);
        AddChild(_performanceOverlay);
        AddChild(_toastHost);
        AddChild(_toolTipHost);
        _host.Root.PointerPressed += HandlePointerPressed;
        _host.Root.PointerPressed += _ =>
        {
            _toolTipHost.Hide();
            _tabPreview.Hide();
        };
        _host.Root.ToolTipTargetChanged += _toolTipHost.Show;
        _host.Root.ToolTipTargetChanged += _tabPreview.Show;
        workspace.ActivePaneChanged += HandleActivePaneChanged;
        workspace.LayoutChanged += HandleLayoutChanged;
        workspace.PaneRatiosChanged += HandlePaneRatiosChanged;
        workspace.PaneActiveTabChanged += HandleActiveTabChanged;
        workspace.PaneSessionStateChanged += HandleSessionStateChanged;
        workspace.PaneTabsChanged += pane => _workspaceView.FindPaneView(pane)?.ApplyTabs();

        ApplyActivePaneState();
    }

    internal event Action? Invalidated
    {
        add => _host.Root.Invalidated += value;
        remove => _host.Root.Invalidated -= value;
    }

    internal event Action<WindowCursor>? CursorChanged
    {
        add => _host.Root.CursorChanged += value;
        remove => _host.Root.CursorChanged -= value;
    }

    internal UiTheme Palette
    {
        get => _host.Palette;
        set => _host.Palette = value;
    }
    internal TimeSpan? NextAnimationFrameDelay =>
        _workspaceView.NextAnimationFrameDelay
        ?? (_performanceOverlay.IsVisible ? PerformanceOverlay.HeartbeatInterval : null);
    internal PaneLayoutArea PaneLayoutArea => new(
        MathF.Max(1.0f, _host.Root.DipsToPixels(_workspaceView.Bounds.Width)),
        MathF.Max(1.0f, _host.Root.DipsToPixels(_workspaceView.Bounds.Height)),
        _host.Root.DipsToPixels(SplitPanel.SplitterSizeDips),
        _chromeVisible ? _host.Root.DipsToPixels(ViewerPaneView.TabRowHeightDips) : 0.0f,
        _host.Root.DipsToPixels(SplitPanel.MinimumPaneSizeDips));

    private ViewerPaneView ActivePaneView => _workspaceView.FindPaneView(_workspace.ActivePane)
        ?? throw new InvalidOperationException("The active pane view is not attached.");

    private void HandleActivePaneChanged(ViewerPane pane)
    {
        _host.Root.ClearPointer();
        _workspaceView.SetActivePane(pane);
        HandleSessionStateChanged(pane);
    }

    private void HandleLayoutChanged(WorkspaceSplit? openingSplit)
    {
        _host.Root.ClearPointer();
        _workspaceView.ApplyLayout(_workspace.Root, openingSplit);
        _workspaceView.SetActivePane(_workspace.ActivePane);
        ApplyTitleBar();
    }

    private void HandlePaneRatiosChanged()
    {
        _host.Root.ClearPointer();
        _workspaceView.ApplyPaneRatios(_workspace.Root);
    }

    private void HandleActiveTabChanged(ViewerPane pane)
    {
        _workspaceView.FindPaneView(pane)?.BindTab(pane.ActiveTab);
        HandleSessionStateChanged(pane);
    }

    private void HandleSessionStateChanged(ViewerPane pane)
    {
        _workspaceView.FindPaneView(pane)?.ApplyState(pane.ActiveSession.State);
        if (ReferenceEquals(pane, _workspace.ActivePane))
        {
            ApplyActivePaneState();
        }

        _host.Root.InvalidateVisual();
    }

    internal void RecreateDeviceResources(ID2D1DeviceContext deviceContext)
    {
        _workspaceView.FinishCollapses();
        _host.RecreateDeviceResources(deviceContext);
        _galleryPanel.RecreateDeviceResources(deviceContext);
        foreach (ViewerPaneView paneView in _workspaceView.PaneViews)
        {
            paneView.RecreateDeviceResources(deviceContext);
        }
    }

    internal void ApplySettings(AppSettings settings)
    {
        if (!settings.KeyBindings.Equals(_keyBindings))
        {
            _keyBindings = settings.KeyBindings;
            _commandPalettePanel.ApplyKeyBindings(_keyBindings);
            _contextMenus.KeyBindings = _keyBindings;
            foreach (ViewerPaneView paneView in _workspaceView.PaneViews)
            {
                paneView.ApplyKeyBindings(_keyBindings);
            }
        }

        _contextMenus.DefaultSort = settings.Sort;
        _galleryEnabled = settings.GalleryEnabled;
        _splitView.SetEdge(GetGalleryEdge(settings.GalleryPlacement));
        _galleryPanel.SetOrientation(_splitView.IsHorizontal
            ? UiOrientation.Vertical
            : UiOrientation.Horizontal);
        _splitView.SetDividerOffset(settings.GallerySizeDips);
        _galleryPanel.SetThumbnailSize(settings.GalleryThumbnailSize);
        _settingsPanel.ApplySettings(settings);
        _sharpPixels = settings.SharpPixelsWhenZoomed;
        foreach (ViewerPaneView paneView in _workspaceView.PaneViews)
        {
            paneView.SharpPixels = _sharpPixels;
        }

        ApplyActivePaneState();
        _host.AnimationsEnabled = settings.AnimationsEnabled;
    }

    internal void ApplyUpdateState(UpdateState state) => _settingsPanel.ApplyUpdateState(state);

    internal void SetFullscreen(bool fullscreen)
    {
        _chromeVisible = !fullscreen;
        foreach (ViewerPaneView paneView in _workspaceView.PaneViews)
        {
            paneView.ChromeVisible = _chromeVisible;
        }

        ApplyActivePaneState();
    }

    /// <summary>Whether a point in window pixels is title bar background, which drags the window.</summary>
    internal bool IsWindowDragAreaAt(PointF pixelPosition) =>
        _chromeVisible
        && pixelPosition.Y < _host.Root.DipsToPixels(Margin.Top + TitleBarHeightDips)
        && _host.Root.IsWindowDragArea(pixelPosition);

    /// <summary>Keeps everything below the part of the window that is off the screen.</summary>
    internal void SetHiddenTop(float dips) => Margin = new UiThickness(0.0f, dips, 0.0f, 0.0f);

    internal void CenterGallerySelection() => _galleryPanel.CenterSelection();

    internal void TogglePerformanceOverlay()
    {
        _performanceOverlay.Toggle();
        _host.Root.InvalidateVisual();
    }

    internal void HandleKey(WindowKeyEvent input)
    {
        // An element holding the keyboard, such as a shortcut being recorded, beats even the window bindings.
        if (_host.Root.HandleCapturedKey(input))
        {
            _host.Root.InvalidateVisual();
            return;
        }

        if (_keyBindings.TryGetCommand(CommandScope.Window, input, out Command? command))
        {
            ExecuteShortcut(command, input, _app.ActiveContext);
            return;
        }

        if (HandleUiKey(input))
        {
            _host.Root.InvalidateVisual();
            return;
        }

        if (input.Key == WindowKey.Escape && !_chromeVisible)
        {
            _app.Execute(AppCommands.ToggleFullscreen, _app.ActiveContext);
            return;
        }

        if (_keyBindings.TryGetCommand(CommandScope.Viewer, input, out command))
        {
            ExecuteShortcut(command, input, CommandContext.For(_workspace.ActiveTab, GetPointerImagePoint()));
        }
    }

    private void ExecuteShortcut(Command command, WindowKeyEvent input, CommandContext context)
    {
        if (!input.IsRepeat || command.RepeatsWhileHeld)
        {
            _app.Execute(command, context);
        }
    }

    /// <summary>Where the pointer is over the active image, in the viewport's pixels.</summary>
    private PointF GetPointerImagePoint()
    {
        PointF pointer = _pointer ?? new PointF(Bounds.Width / 2.0f, Bounds.Height / 2.0f);
        RectangleF imageBounds = ActivePaneView.GetImageBoundsRelativeTo(this);
        return new PointF(
            _host.Root.DipsToPixels(pointer.X - imageBounds.X),
            _host.Root.DipsToPixels(pointer.Y - imageBounds.Y));
    }

    private bool HandleUiKey(WindowKeyEvent input)
    {
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
            _host.Root.HandleKey(
                input,
                _modalHost,
                wrapFocus: true,
                directionalNavigation: true);
            return true;
        }

        return _host.Root.HandleKey(
            input,
            ActivePaneView.FocusScope,
            wrapFocus: false,
            directionalNavigation: false);
    }

    internal void HandleTextInput(string text)
    {
        if (_host.Root.HandleTextInput(text))
        {
            _host.Root.InvalidateVisual();
        }
    }

    internal void SetDpi(float dpi) => _host.Root.SetDpi(dpi);

    internal void BeginResize() => _host.Root.BeginResize();

    internal void EndResize() => _host.Root.EndResize();

    internal bool Update()
    {
        bool continues = _host.Update();
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

    internal void HandlePointer(WindowPointerEvent input)
    {
        if (input.Kind is not (WindowPointerEventKind.Cancelled or WindowPointerEventKind.Left))
        {
            _pointer = new PointF(
                UiDpi.PixelsToDips(input.Position.X, _host.Root.Dpi),
                UiDpi.PixelsToDips(input.Position.Y, _host.Root.Dpi));
        }

        _host.Root.HandlePointer(input);
    }

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
        _dragOverlay.Dispose();
        _toastHost.Dispose();
        _tabPreview.Dispose();
        _galleryPanel.Dispose();
        _workspaceView.Dispose();
    }

    private void HandlePointerPressed(UiElement? target)
    {
        for (UiElement? element = target; element is not null; element = element.Parent)
        {
            if (element is ViewerPaneView paneView)
            {
                _app.SelectPane(paneView.Pane);
                return;
            }
        }
    }

    internal void ShowSettings()
    {
        ShowModal(_settingsPanel);
    }

    internal void ShowCommandPalette()
    {
        _commandPalettePanel.Reset();
        ShowModal(_commandPalettePanel);
    }

    private void ShowModal(ModalContent content)
    {
        _popupHost.Close();
        _modalHost.Show(content, CloseModal);
        _host.Root.SetFocus(content.InitialFocus);
    }

    private void CloseModal()
    {
        if (!_modalHost.IsOpen)
        {
            return;
        }

        _host.Root.SetFocus(null);
        _popupHost.Close();
        _modalHost.Close();
    }

    private void ApplyActivePaneState()
    {
        ViewerSessionState state = _workspace.ActiveSession.State;
        _splitView.SecondPaneVisible = _chromeVisible && _galleryEnabled && ShouldShowGallery(state);
        _galleryPanel.ApplyState(_workspace.ActiveTab, state);
        ApplyTitleBar();
    }

    // Only bare background reaches this element.
    internal override bool IsWindowDragArea(PointF position) => true;

    // The panes along the top hold the title bar in their tab rows. The gallery instead keeps below
    // it, leaving bare title bar above itself, and the panes give way to the window buttons unless
    // the gallery sits under them.
    private void ApplyTitleBar()
    {
        _windowButtons.IsVisible = _chromeVisible;
        _menuButton.IsVisible = _chromeVisible;
        bool galleryBelowTitleBar = _chromeVisible && _splitView.Edge != SplitViewEdge.Bottom;
        _galleryPanel.Margin = new UiThickness(
            0.0f,
            galleryBelowTitleBar ? TitleBarHeightDips - UiDesign.WindowMargin : 0.0f,
            0.0f,
            0.0f);

        bool galleryShown = _splitView.SecondPaneVisible;
        bool workspaceAtTop = _chromeVisible && !(galleryShown && _splitView.Edge == SplitViewEdge.Top);
        bool workspaceAtLeft = !(galleryShown && _splitView.Edge == SplitViewEdge.Left);
        bool workspaceAtRight = !(galleryShown && _splitView.Edge == SplitViewEdge.Right);
        _workspaceView.SetTitleBar(workspaceAtTop
            ? new TitleBarInsets(
                workspaceAtLeft ? MenuButtonMargin.Left + MenuButtonSizeDips : 0.0f,
                workspaceAtRight ? WindowButtons.WidthDips : 0.0f)
            : null);
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

}

internal readonly record struct ViewerTabInfo(string Label, string? ImagePath)
{
    internal static ViewerTabInfo For(ViewerTab tab)
    {
        string? path = tab.Session.State.RequestedPath;
        return new ViewerTabInfo(Path.GetFileName(path) ?? "New tab", path);
    }
}
