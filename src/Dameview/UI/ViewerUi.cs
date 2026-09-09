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
using Dameview.Viewing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI;

internal sealed class ViewerUi : UiElement, IDisposable
{
    private readonly ID2D1DeviceContext _deviceContext;
    private readonly ID2D1SolidColorBrush _brush;
    private readonly List<ViewerPaneView> _paneViews;
    private readonly TabPreview _tabPreview;
    private readonly Overlay _mainOverlay;
    private readonly SplitView _splitView;
    private readonly ToolbarPanel _toolbarPanel;
    private readonly GalleryPanel _galleryPanel;
    private readonly SettingsPanel _settingsPanel;
    private readonly ModalHost _modalHost;
    private readonly PopupHost _popupHost;
    private readonly UiAnimationClock _animationClock;
    private readonly UiRoot _root;
    private ViewerPane _activePane;
    private ViewerPaneView _activePaneView;

    internal ViewerUi(
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        ViewerPane pane,
        float dpi,
        UiTheme theme,
        IViewerCommands commands,
        IThumbnailLoader thumbnailLoader,
        Action<Theme> setTheme,
        Action<FolderSort> setSort,
        TimeProvider? timeProvider = null,
        UiPost? postToUi = null)
    {
        _deviceContext = deviceContext;
        _brush = deviceContext.CreateSolidColorBrush(default(Color4));
        Palette = theme;
        _animationClock = new UiAnimationClock(timeProvider);
        _activePane = pane;
        _tabPreview = new TabPreview(deviceContext, thumbnailLoader);
        var paneView = new ViewerPaneView(
            deviceContext,
            directWriteFactory,
            pane,
            index => commands.SelectTab(pane, index),
            index => commands.CloseTab(pane, index),
            ShowSettings,
            ShowTabPreview,
            timeProvider,
            postToUi);
        _paneViews = [paneView];
        _activePaneView = paneView;
        _toolbarPanel = new ToolbarPanel(directWriteFactory, commands, ShowSettings);
        _galleryPanel = new GalleryPanel(
            deviceContext,
            directWriteFactory,
            thumbnailLoader,
            commands.OpenImage,
            commands.OpenImageInNewTab);
        _galleryPanel.Bind(pane.ActiveTab.GalleryState);
        _mainOverlay = new Overlay(paneView, _toolbarPanel);
        _splitView = new SplitView(
            _mainOverlay,
            _galleryPanel,
            initialDividerOffsetDips: GalleryPanel.DefaultWidthDips);
        _modalHost = new ModalHost(CloseSettings);
        _popupHost = new PopupHost();
        _settingsPanel = new SettingsPanel(
            directWriteFactory,
            _popupHost,
            CloseSettings,
            setTheme,
            setSort);

        AddChild(_splitView);
        AddChild(_tabPreview);
        AddChild(_modalHost);
        AddChild(_popupHost);
        _root = new UiRoot(this, dpi);
        _root.CursorChanged += cursor => _cursorChanged?.Invoke(cursor);

        ViewerSessionState state = pane.ActiveSession.State;
        bool hasImage = paneView.HasImage;
        _toolbarPanel.IsVisible = hasImage;
        _galleryPanel.IsVisible = state.FolderEntries.Length > 0;
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

    internal TimeSpan? NextAnimationFrameDelay
    {
        get
        {
            TimeSpan? nextDelay = null;
            foreach (ViewerPaneView paneView in _paneViews)
            {
                if (!paneView.IsVisible || paneView.NextAnimationFrameDelay is not { } delay)
                {
                    continue;
                }

                nextDelay = nextDelay is null || delay < nextDelay ? delay : nextDelay;
            }

            return nextDelay;
        }
    }

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

        ViewerSessionState state = pane.ActiveSession.State;
        _galleryPanel.Bind(pane.ActiveTab.GalleryState);
        ApplyActivePaneState(state, showToolbar: false);
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

    internal void ApplySettings(AppSettings settings) => _settingsPanel.ApplySettings(settings);

    internal void ApplyTabs(ViewerPane pane, IReadOnlyList<ViewerTabInfo> tabs, int selectedIndex)
    {
        FindPaneView(pane)?.ApplyTabs(tabs, selectedIndex);
    }

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

            _root.HandleKey(input, _settingsPanel, wrapFocus: true, directionalNavigation: true);
            return true;
        }

        UiElement focusScope = _toolbarPanel.IsVisible ? _toolbarPanel : _activePaneView.EmptyStateFocusScope;
        return _root.HandleKey(input, focusScope, wrapFocus: false, directionalNavigation: false);
    }

    internal void SetDpi(float dpi) => _root.SetDpi(dpi);

    internal bool Update()
    {
        UiUpdateContext context = _animationClock.GetNextFrame();
        bool continues = _root.Update(context);
        if (!continues && NextAnimationFrameDelay is null)
        {
            _animationClock.Reset();
        }

        return continues;
    }

    internal void DrawFrame(SizeF pixelSize)
    {
        foreach (ViewerPaneView paneView in _paneViews)
        {
            if (paneView.IsVisible)
            {
                paneView.UpdateStatus();
            }
        }

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
        _settingsPanel.Dispose();
        _toolbarPanel.Dispose();
        _galleryPanel.Dispose();
        foreach (ViewerPaneView paneView in _paneViews)
        {
            paneView.Dispose();
        }

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

    private void ShowSettings()
    {
        _root.ClearPointer();
        _root.SetFocus(null);
        _popupHost.Close();
        _modalHost.Show(_settingsPanel);
        _root.SetFocus(_settingsPanel.InitialFocus);
    }

    private void CloseSettings()
    {
        if (!_modalHost.IsOpen)
        {
            return;
        }

        _root.ClearPointer();
        _root.SetFocus(null);
        _popupHost.Close();
        _modalHost.Close();
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

    private void ApplyActivePaneState(ViewerSessionState state, bool showToolbar)
    {
        bool hasImage = state.DisplayedImage is not null;
        _toolbarPanel.IsVisible = hasImage;
        if (showToolbar)
        {
            _toolbarPanel.Show();
        }

        _galleryPanel.IsVisible = state.FolderEntries.Length > 0;
        _splitView.SecondPaneVisible = _galleryPanel.IsVisible;
        _galleryPanel.ApplyState(state.FolderEntries, state.RequestedPath);
    }

    private ViewerPaneView? FindPaneView(ViewerPane pane)
    {
        return _paneViews.FirstOrDefault(candidate => ReferenceEquals(candidate.Pane, pane));
    }
}

internal readonly record struct ViewerTabInfo(string Label, string? ImagePath);
