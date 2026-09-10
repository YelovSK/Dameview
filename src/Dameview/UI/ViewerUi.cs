using System.Drawing;
using Dameview.Commands;
using Dameview.Imaging;
using Dameview.Navigation;
using Dameview.Platform;
using Dameview.Settings;
using Dameview.UI.Animation;
using Dameview.UI.Components;
using Dameview.UI.Layout;
using Dameview.UI.Panels;
using Dameview.Updates;
using Dameview.Viewing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI;

internal sealed class ViewerUi : UiElement, IDisposable
{
    private readonly ID2D1DeviceContext _deviceContext;
    private readonly ID2D1SolidColorBrush _brush;
    private readonly WorkspaceView _workspaceView;
    private readonly TabPreview _tabPreview;
    private readonly Overlay _mainOverlay;
    private readonly SplitView _splitView;
    private readonly ToolbarPanel _toolbarPanel;
    private readonly GalleryPanel _galleryPanel;
    private readonly SettingsPanel _settingsPanel;
    private readonly CommandPalettePanel _commandPalettePanel;
    private readonly ModalHost _modalHost;
    private readonly PopupHost _popupHost;
    private readonly UiAnimationClock _animationClock;
    private readonly UiRoot _root;
    private readonly Action<ViewerPane> _selectPane;
    private ViewerPane _activePane;
    private ViewerPaneView _activePaneView;
    private bool _animationsEnabled = true;

    internal ViewerUi(
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        ViewerWorkspace workspace,
        float dpi,
        UiTheme theme,
        IAppCommands commands,
        IThumbnailLoader thumbnailLoader,
        TimeProvider? timeProvider = null,
        UiPost? postToUi = null)
    {
        _deviceContext = deviceContext;
        _brush = deviceContext.CreateSolidColorBrush(default(Color4));
        _selectPane = commands.SelectPane;
        Palette = theme;
        _animationClock = new UiAnimationClock(timeProvider);
        _activePane = workspace.ActivePane;
        _tabPreview = new TabPreview(deviceContext, thumbnailLoader);
        _workspaceView = new WorkspaceView(
            workspace.Root,
            pane => new ViewerPaneView(
                deviceContext,
                directWriteFactory,
                pane,
                index => commands.SelectTab(pane, index),
                index => commands.CloseTab(pane, index),
                () => commands.DuplicateActiveTab(pane),
                ShowSettings,
                ShowTabPreview,
                timeProvider,
                postToUi));
        _activePaneView = FindPaneView(_activePane)
            ?? throw new InvalidOperationException("The active pane view was not created.");
        _workspaceView.SetActivePane(_activePane);
        _toolbarPanel = new ToolbarPanel(directWriteFactory, commands, ShowSettings);
        _galleryPanel = new GalleryPanel(
            deviceContext,
            directWriteFactory,
            thumbnailLoader,
            commands.OpenImage,
            commands.OpenImageInNewTab);
        _galleryPanel.Bind(_activePane.ActiveTab.GalleryState);
        _mainOverlay = new Overlay(_workspaceView, _toolbarPanel);
        _splitView = new SplitView(
            _mainOverlay,
            _galleryPanel,
            initialDividerOffsetDips: GalleryPanel.DefaultWidthDips);
        _modalHost = new ModalHost();
        _popupHost = new PopupHost();
        _settingsPanel = new SettingsPanel(
            directWriteFactory,
            _popupHost,
            CloseSettings,
            commands);
        _commandPalettePanel = new CommandPalettePanel(
            directWriteFactory,
            ViewerCommandCatalog.Commands,
            command =>
            {
                CloseCommandPalette();
                commands.ExecuteCommand(command);
            });

        AddChild(_splitView);
        AddChild(_tabPreview);
        AddChild(_modalHost);
        AddChild(_popupHost);
        _root = new UiRoot(this, dpi);
        _root.CursorChanged += cursor => _cursorChanged?.Invoke(cursor);
        _root.PointerPressed += HandlePointerPressed;

        ViewerSessionState state = _activePane.ActiveSession.State;
        bool hasImage = _activePaneView.HasImage;
        _toolbarPanel.IsVisible = hasImage;
        _galleryPanel.IsVisible = ShouldShowGallery(state);
        _splitView.SecondPaneVisible = _galleryPanel.IsVisible;
        _galleryPanel.ApplyState(state.FolderEntries, state.RequestedPath);
        if (hasImage)
        {
            _toolbarPanel.Show();
        }
    }

    internal event Action? Invalidated
    {
        add => _root.Invalidated += value;
        remove => _root.Invalidated -= value;
    }

    private Action<UiCursor>? _cursorChanged;

    internal event Action<UiCursor>? CursorChanged
    {
        add => _cursorChanged += value;
        remove => _cursorChanged -= value;
    }

    internal UiTheme Palette { get; set; }
    internal string? SettingsError
    {
        get => _settingsPanel.Error;
        set
        {
            if (_settingsPanel.Error == value)
            {
                return;
            }

            _settingsPanel.Error = value;
            _activePaneView.SettingsError = value;
            _root.InvalidateVisual();
        }
    }

    internal TimeSpan? NextAnimationFrameDelay => _workspaceView.NextAnimationFrameDelay;
    internal bool IsClosingPane => _workspaceView.IsClosingPane;
    internal float GalleryWidthDips => _splitView.DividerOffsetDips;

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
            if (!ReferenceEquals(paneView, _activePaneView))
            {
                _activePaneView.SettingsError = null;
            }

            _activePaneView = paneView;
            paneView.SettingsError = _settingsPanel.Error;
        }

        _workspaceView.SetActivePane(pane);

        ViewerSessionState state = pane.ActiveSession.State;
        _galleryPanel.Bind(pane.ActiveTab.GalleryState);
        ApplyActivePaneState(state, showToolbar: false);
    }

    internal void ApplyLayout(WorkspaceNode root, WorkspaceSplit? openingSplit)
    {
        _root.ClearPointer();
        _tabPreview.Hide();
        _workspaceView.ApplyLayout(root, openingSplit);
        _activePaneView = FindPaneView(_activePane)
            ?? throw new InvalidOperationException("The active pane view is not attached.");
        _activePaneView.SettingsError = _settingsPanel.Error;
        _workspaceView.SetActivePane(_activePane);
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
            _galleryPanel.Bind(tab.GalleryState);
        }
    }

    internal void ApplyState(ViewerPane pane, ViewerSessionState state)
    {
        ViewerPaneView? paneView = FindPaneView(pane);
        bool hadDisplayedImage = paneView?.HasImage == true;
        bool isActivePane = ReferenceEquals(pane, _activePane);
        paneView?.ApplyState(state, clearPointer: isActivePane);
        if (isActivePane)
        {
            ApplyActivePaneState(state, showToolbar: state.DisplayedImage is not null && !hadDisplayedImage);
        }

        _root.InvalidateVisual();
    }

    internal void ApplySettings(AppSettings settings)
    {
        _splitView.SetDividerOffset(settings.GalleryWidthDips);
        _settingsPanel.ApplySettings(settings);
        if (_animationsEnabled == settings.AnimationsEnabled)
        {
            return;
        }

        _animationsEnabled = settings.AnimationsEnabled;
        _animationClock.Reset();
        _root.InvalidateVisual();
    }

    internal void ApplyUpdateState(UpdateState state) => _settingsPanel.ApplyUpdateState(state);

    internal void ApplyTabs(ViewerPane pane, IReadOnlyList<ViewerTabInfo> tabs, int selectedIndex)
    {
        FindPaneView(pane)?.ApplyTabs(tabs, selectedIndex);
    }

    internal void CenterGallerySelection() => _galleryPanel.CenterSelection();

    internal bool HandleKey(UiKeyEvent input)
    {
        if (_popupHost.IsOpen)
        {
            if (input.Key == UiKey.Escape)
            {
                return _popupHost.HandleEscape();
            }

            if (input.Key == UiKey.Tab)
            {
                _popupHost.Close();
            }
        }

        if (_modalHost.IsOpen)
        {
            if (input.Key == UiKey.Escape)
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

        UiElement focusScope = _toolbarPanel.IsVisible ? _toolbarPanel : _activePaneView.EmptyStateFocusScope;
        return _root.HandleKey(input, focusScope, wrapFocus: false, directionalNavigation: false);
    }

    internal bool HandleTextInput(string text) => _root.HandleTextInput(text);

    internal void SetDpi(float dpi) => _root.SetDpi(dpi);

    internal bool Update()
    {
        UiUpdateContext context = _animationClock.GetNextFrame(_animationsEnabled);
        bool continues = _root.Update(context);
        _workspaceView.CompletePendingClose();
        if (!continues && NextAnimationFrameDelay is null)
        {
            _animationClock.Reset();
        }

        return continues;
    }

    internal void DrawFrame(SizeF pixelSize)
    {
        _workspaceView.UpdateStatuses();

        var context = new UiDrawContext(_deviceContext, _brush, Palette, _root.Dpi);
        _root.Draw(context, pixelSize);
    }

    internal bool HandlePointer(in UiPointerEvent input) => _root.HandlePointer(input);

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        _splitView.Measure(availableSize);
        _tabPreview.Measure(availableSize);
        _modalHost.Measure(availableSize);
        _popupHost.Measure(availableSize);
        return availableSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        _splitView.Arrange(new RectangleF(PointF.Empty, finalSize));
        RectangleF paneBounds = _activePaneView.GetBoundsRelativeTo(_mainOverlay);
        RectangleF contentBounds = _activePaneView.ContentBounds;
        contentBounds.Offset(paneBounds.Location);
        var layout = ViewerLayout.Calculate(
            contentBounds.Size,
            showStatus: false,
            showToolbar: _toolbarPanel.IsVisible,
            toolbarWidthDips: ToolbarPanel.WidthDips);
        RectangleF toolbarBounds = layout.Toolbar;
        toolbarBounds.Offset(contentBounds.Location);
        _toolbarPanel.Arrange(toolbarBounds);
        _tabPreview.Arrange(new RectangleF(PointF.Empty, finalSize));
        _modalHost.Arrange(new RectangleF(PointF.Empty, finalSize));
        _popupHost.Arrange(new RectangleF(PointF.Empty, finalSize));
    }

    public void Dispose()
    {
        _root.ClearPointer();
        _root.SetFocus(null);
        _modalHost.Close();
        _popupHost.Close();
        _tabPreview.Dispose();
        _commandPalettePanel.Dispose();
        _settingsPanel.Dispose();
        _toolbarPanel.Dispose();
        _galleryPanel.Dispose();
        _workspaceView.Dispose();
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
        ShowModal(_settingsPanel, CloseSettings);
    }

    internal void ShowCommandPalette()
    {
        _commandPalettePanel.Reset();
        ShowModal(_commandPalettePanel, CloseCommandPalette);
    }

    private void CloseSettings()
    {
        if (!CloseModal())
        {
            return;
        }

        if (_toolbarPanel.IsVisible)
        {
            _root.SetFocus(_toolbarPanel.SettingsButton);
            _toolbarPanel.Show();
        }
        else
        {
            _root.SetFocus(_activePaneView.EmptyStateSettingsButton);
        }
    }

    private void CloseCommandPalette()
    {
        CloseModal();
    }

    private void ShowModal(ModalContent content, Action dismiss)
    {
        _root.ClearPointer();
        _root.SetFocus(null);
        _popupHost.Close();
        _modalHost.Show(content, dismiss);
        _root.SetFocus(content.InitialFocus);
    }

    private bool CloseModal()
    {
        if (!_modalHost.IsOpen)
        {
            return false;
        }

        _root.ClearPointer();
        _root.SetFocus(null);
        _popupHost.Close();
        _modalHost.Close();
        return true;
    }

    private void ApplyActivePaneState(ViewerSessionState state, bool showToolbar)
    {
        bool hasImage = state.DisplayedImage is not null;
        _toolbarPanel.IsVisible = hasImage;
        if (showToolbar)
        {
            _toolbarPanel.Show();
        }

        _galleryPanel.IsVisible = ShouldShowGallery(state);
        _splitView.SecondPaneVisible = _galleryPanel.IsVisible;
        _galleryPanel.ApplyState(state.FolderEntries, state.RequestedPath);
    }

    private static bool ShouldShowGallery(ViewerSessionState state)
    {
        return state.RequestedPath is not null
            && !state.IsError
            && state.FolderError is null;
    }

    private ViewerPaneView? FindPaneView(ViewerPane pane)
    {
        return _workspaceView.FindPaneView(pane);
    }
}

internal readonly record struct ViewerTabInfo(string Label, string? ImagePath);
