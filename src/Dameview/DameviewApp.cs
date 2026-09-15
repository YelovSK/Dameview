using System.Drawing;
using Dameview.Commands;
using Dameview.Imaging.Decoding;
using Dameview.Imaging.Loading;
using Dameview.Installation;
using Dameview.Navigation;
using Dameview.Rendering;
using Dameview.Settings;
using Dameview.UI;
using Dameview.UI.Foundation;
using Dameview.UI.Presentation;
using Dameview.Updates;
using Dameview.Viewing;
using Dameview.Win32;
using Dameview.Win32.Input;

namespace Dameview;

internal sealed class DameviewApp : IAppCommands, IDisposable
{
    private const long RenderBitmapCacheCapacityBytes = 256L * 1024L * 1024L;
    private const long ThumbnailBitmapCacheCapacityBytes = 64L * 1024L * 1024L;

    private readonly AppWindow _window;
    private readonly SynchronizationContext _uiContext;
    private readonly D2DRenderer _renderer;
    private readonly ViewerUi _ui;
    private readonly WindowsImageLoadingBackend _imageBackend;
    private readonly ThumbnailCoordinator _thumbnailCoordinator;
    private readonly ImageInfoLoader _imageInfoLoader;
    private readonly ImageLoadService _imageLoadService;
    private readonly RenderBitmapCache _renderBitmapCache;
    private readonly RenderBitmapCache _thumbnailBitmapCache;
    private readonly ThumbnailImageLoader _thumbnailImageLoader;
    private readonly IFolderScanner _folderScanner;
    private readonly ViewerWorkspace _workspace;
    private readonly SettingsService _settings;
    private readonly UpdateService _updates;
    private CancellationTokenSource? _copyImageCancellation;
    private int _pointerX;
    private int _pointerY;

    public DameviewApp()
    {
        _window = new AppWindow("Dameview", 1100, 720);
        _uiContext = new WindowSynchronizationContext(_window.Post);
        SynchronizationContext.SetSynchronizationContext(_uiContext);
        _window.SetTitleBarTheme(dark: true, UiTheme.Default.WindowCaptionColor, UiTheme.Default.WindowTextColor);
        _pointerX = _window.ClientWidth / 2;
        _pointerY = _window.ClientHeight / 2;
        _renderer = new D2DRenderer(
            _window.Handle,
            _window.ClientWidth,
            _window.ClientHeight,
            _window.Dpi);
        _imageBackend = new WindowsImageLoadingBackend();
        _thumbnailCoordinator = new ThumbnailCoordinator(
            _imageBackend.LoadThumbnail,
            _uiContext);
        _imageInfoLoader = new ImageInfoLoader(_imageBackend.CreateDecoder);
        _imageLoadService = new ImageLoadService(
            _uiContext,
            _imageBackend,
            new ImageRepresentationPolicy(checked((int)_renderer.DeviceContext.MaximumBitmapSize)),
            _imageInfoLoader);
        _renderBitmapCache = new RenderBitmapCache(RenderBitmapCacheCapacityBytes);
        _thumbnailBitmapCache = new RenderBitmapCache(ThumbnailBitmapCacheCapacityBytes);
        _thumbnailImageLoader = new ThumbnailImageLoader(
            _thumbnailCoordinator,
            _thumbnailBitmapCache,
            _renderer.DeviceContext,
            _uiContext);
        using var imageDecoder = new ImageDecoder();
        HashSet<string> extensions = imageDecoder.GetProbablySupportedExtensions();
        _folderScanner = new FolderScanner(path => extensions.Contains(Path.GetExtension(path)));
        _workspace = new ViewerWorkspace(CreateTab);
        _settings = new SettingsService(SettingsService.DefaultPath, _uiContext);
        _updates = new UpdateService(
            new GitHubUpdateClient(),
            _uiContext,
            AppInstallation.GetInstalledRunningVersion());
        _ui = new ViewerUi(
            _renderer.DeviceContext,
            _renderer.DirectWriteFactory,
            _workspace,
            _window.Dpi,
            UiTheme.Default,
            this,
            _thumbnailImageLoader);
        _ui.Invalidated += _window.RequestRepaint;
        _ui.CursorChanged += _window.ApplyCursor;
        _workspace.ActivePaneChanged += HandleActivePaneChanged;
        _workspace.LayoutChanged += HandleLayoutChanged;
        _workspace.PaneRatiosChanged += HandlePaneRatiosChanged;
        _workspace.PaneActiveTabChanged += HandleActiveTabChanged;
        _workspace.PaneSessionStateChanged += HandleSessionChanged;
        _workspace.PaneTabsChanged += HandleTabsChanged;
        HandleTabsChanged(_workspace.ActivePane);

        _window.RenderFrame += HandleRenderFrame;
        _window.Resized += HandleResize;
        _window.DpiChanged += HandleDpiChanged;
        _window.FilesDropped += HandleFilesDropped;
        _window.KeyPressed += HandleKeyPress;
        _window.TextInput += HandleTextInput;
        _window.PointerInput += HandlePointerInput;

        _settings.Changed += ApplySettings;
        _settings.ErrorChanged += () =>
        {
            _ui.SettingsError = _settings.Error;
            _window.RequestRepaint();
        };
        _updates.Changed += HandleUpdateChanged;
        _updates.UpdateDownloaded += HandleUpdateDownloaded;
        _ui.ApplyUpdateState(_updates.State);
    }

    public int Run(string[] args)
    {
        _settings.Start();

        if (args.FirstOrDefault() is string imagePath)
        {
            _workspace.OpenImage(imagePath);
        }

        _window.Closed += NativeMethods.RequestMessageLoopExit;
        try
        {
            return _window.Run(_renderer.FrameLatencyWaitHandle);
        }
        finally
        {
            _window.Closed -= NativeMethods.RequestMessageLoopExit;
        }
    }

    private void HandleFilesDropped(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        _workspace.OpenImage(paths[0]);
        for (int index = 1; index < paths.Count; index++)
        {
            _workspace.OpenImageInNewTab(paths[index]);
        }
    }

    public void Dispose()
    {
        // WINDOWPLACEMENT keeps rcNormalPosition up to date while maximized,
        // so this also remembers the size that will be restored after unmaximizing.
        _settings.Update(_settings.Current with
        {
            GallerySizeDips = _ui.GallerySizeDips,
            Window = _window.CapturePlacement(),
        });
        _updates.Changed -= HandleUpdateChanged;
        _updates.UpdateDownloaded -= HandleUpdateDownloaded;
        _settings.Dispose();
        _copyImageCancellation?.Cancel();
        _copyImageCancellation?.Dispose();
        _ui.Invalidated -= _window.RequestRepaint;
        _ui.Dispose();
        _workspace.Dispose();
        _renderBitmapCache.Dispose();
        _thumbnailBitmapCache.Dispose();
        _imageLoadService.Dispose();
        _imageInfoLoader.Dispose();
        _thumbnailCoordinator.Dispose();
        _renderer.Dispose();
        _window.Dispose();
    }

    public void ShowPreviousImage()
    {
        _workspace.ActiveSession.ShowPreviousImage();
    }

    public void ShowNextImage()
    {
        _workspace.ActiveSession.ShowNextImage();
    }

    public void FitImage()
    {
        if (_workspace.ActiveSession.Animator.Fit())
        {
            _window.RequestRepaint();
        }
    }

    private void ShowActualSize(PointF anchor)
    {
        if (_workspace.ActiveSession.Animator.ShowActualSizeAt(anchor.X, anchor.Y))
        {
            _window.RequestRepaint();
        }
    }

    public void ShowActualSize()
    {
        ShowActualSize(_workspace.ActiveSession.Viewport.ViewportCenter);
    }

    public void SplitRight()
    {
        if (!_ui.IsClosingPane)
        {
            _workspace.SplitPane(_workspace.ActivePane, WorkspaceSplitOrientation.Horizontal);
        }
    }

    public void SplitDown()
    {
        if (!_ui.IsClosingPane)
        {
            _workspace.SplitPane(_workspace.ActivePane, WorkspaceSplitOrientation.Vertical);
        }
    }

    public void OpenImage(string path)
    {
        _workspace.SelectImage(path);
    }

    public void OpenImageInNewTab(string path)
    {
        _workspace.OpenImageInNewTab(path);
    }

    public void OpenImageInNewTab(string path, WorkspaceDropTarget target)
    {
        _workspace.OpenImageInNewTab(path, target);
    }

    public bool MoveTab(ViewerPane sourcePane, ViewerTab tab, WorkspaceDropTarget target)
    {
        return _workspace.MoveTab(sourcePane, tab, target);
    }

    public void SelectPane(ViewerPane pane)
    {
        _workspace.SelectPane(pane);
    }

    public void DuplicateActiveTab(ViewerPane pane)
    {
        _workspace.DuplicateActiveTab(pane);
    }

    public void SelectTab(ViewerPane pane, int index)
    {
        _workspace.SelectTab(pane, index);
    }

    public void CloseTab(ViewerPane pane, int index)
    {
        CloseTabOrPane(pane, index);
    }

    private void HandleKeyPress(WindowKeyEvent input)
    {
        if (ViewerKeyBindings.TryGetCommand(ViewerKeyBindings.Window, input, out ViewerCommandId command))
        {
            ExecuteCommand(command);
            return;
        }

        if (_ui.HandleKey(input))
        {
            _window.RequestRepaint();
            return;
        }

        if (input.Key == WindowKey.Escape && _window.IsFullscreen)
        {
            ToggleFullscreen();
            return;
        }

        if (ViewerKeyBindings.TryGetCommand(ViewerKeyBindings.Viewer, input, out command))
        {
            if (command == ViewerCommandId.ShowActualSize)
            {
                ShowActualSize(_ui.GetImageViewportPoint(new PointF(_pointerX, _pointerY)));
                return;
            }

            ExecuteCommand(command);
        }
    }

    private void HandleTextInput(string text)
    {
        if (_ui.HandleTextInput(text))
        {
            _window.RequestRepaint();
        }
    }

    public void ExecuteCommand(ViewerCommandId command)
    {
        switch (command)
        {
            case ViewerCommandId.NewTab:
                _workspace.DuplicateActiveTab(_workspace.ActivePane);
                break;

            case ViewerCommandId.CloseTab:
                CloseTabOrPane(_workspace.ActivePane, _workspace.ActiveIndex);
                break;

            case ViewerCommandId.PreviousTab:
                _workspace.SelectRelativeTab(-1);
                break;

            case ViewerCommandId.NextTab:
                _workspace.SelectRelativeTab(1);
                break;

            case ViewerCommandId.PreviousImage:
                ShowPreviousImage();
                _ui.CenterGallerySelection();
                break;

            case ViewerCommandId.NextImage:
                ShowNextImage();
                _ui.CenterGallerySelection();
                break;

            case ViewerCommandId.FitImage:
                FitImage();
                break;

            case ViewerCommandId.ShowActualSize:
                ShowActualSize();
                break;

            case ViewerCommandId.CopyImage:
                CopyImage();
                break;

            case ViewerCommandId.ToggleFullscreen:
                ToggleFullscreen();
                break;

            case ViewerCommandId.SplitRight:
                SplitRight();
                break;

            case ViewerCommandId.SplitDown:
                SplitDown();
                break;

            case ViewerCommandId.EqualizePanes:
                _workspace.EqualizePanes();
                break;

            case ViewerCommandId.OptimizePaneLayout:
                if (!_ui.IsClosingPane)
                {
                    _workspace.OptimizePaneLayout(_ui.PaneLayoutArea);
                }

                break;

            case ViewerCommandId.ShowSettings:
                _ui.ShowSettings();
                break;

            case ViewerCommandId.ShowCommandPalette:
                _ui.ShowCommandPalette();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }

    private void CopyImage()
    {
        string? path = _workspace.ActiveSession.State.DisplayedImage?.Path;
        if (path is null)
        {
            return;
        }

        _copyImageCancellation?.Cancel();
        _copyImageCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _copyImageCancellation = cancellation;

        _imageLoadService.DecodeTemporary(path, (image, error) =>
        {
            try
            {
                if (cancellation.IsCancellationRequested)
                {
                    return;
                }

                if (error is not null)
                {
                    return;
                }

                if (image is null)
                {
                    return;
                }

                if (!Win32Clipboard.TrySetImage(
                        _window.Handle,
                        image.Width,
                        image.Height,
                        image.Stride,
                        image.Span))
                {
                    return;
                }
            }
            finally
            {
                image?.Dispose();
                if (ReferenceEquals(_copyImageCancellation, cancellation))
                {
                    _copyImageCancellation = null;
                    cancellation.Dispose();
                }
            }
        }, cancellation.Token);
    }

    public void ActivateUpdate() => _updates.Activate();

    public void SetTheme(ThemeId theme) => _settings.Update(_settings.Current with { Theme = theme });

    public void SetAnimationsEnabled(bool enabled) =>
        _settings.Update(_settings.Current with { AnimationsEnabled = enabled });

    public void SetGalleryEnabled(bool enabled) =>
        _settings.Update(_settings.Current with { GalleryEnabled = enabled });

    public void SetGalleryPlacement(GalleryPlacement placement) =>
        _settings.Update(_settings.Current with { GalleryPlacement = placement });

    public void SetGalleryThumbnailSize(GalleryThumbnailSize size) =>
        _settings.Update(_settings.Current with { GalleryThumbnailSize = size });

    public void SetSort(FolderSort sort) => _settings.Update(_settings.Current with { Sort = sort });

    private void ToggleFullscreen()
    {
        _window.ToggleFullscreen();
        _ui.SetFullscreen(_window.IsFullscreen);
    }

    private void ApplySettings(AppSettings previous, AppSettings current)
    {
        if (current.Window is { } windowPlacement && previous.Window != windowPlacement)
        {
            _window.RestorePlacement(windowPlacement);
        }

        _ui.ApplySettings(current);
        if (previous.Theme != current.Theme)
        {
            Theme theme = Themes.Get(current.Theme);
            _ui.Palette = theme.Palette;
            _window.SetTitleBarTheme(theme.IsDark, theme.Palette.WindowCaptionColor, theme.Palette.WindowTextColor);
        }

        if (previous.Sort != current.Sort)
        {
            _workspace.SetSort(current.Sort);
        }

        _window.RequestRepaint();
    }

    private void HandleUpdateChanged(UpdateState state)
    {
        _ui.ApplyUpdateState(state);
        _window.RequestRepaint();
    }

    private void HandleUpdateDownloaded(string path)
    {
        AppUpdateApplier.Launch(path);
        _window.Close();
    }

    private void HandleSessionChanged(ViewerPane pane)
    {
        ViewerSessionState state = pane.ActiveSession.State;
        _ui.ApplyState(pane, state);
        if (ReferenceEquals(pane, _workspace.ActivePane))
        {
            string fileName = state.RequestedPath is null
                ? "Dameview"
                : Path.GetFileName(state.RequestedPath);
            _window.SetTitle($"{fileName} — Dameview");
        }

        _window.RequestRepaint();
    }

    private void HandleActiveTabChanged(ViewerPane pane)
    {
        _ui.BindTab(pane, pane.ActiveTab);
        HandleSessionChanged(pane);
    }

    private void HandleActivePaneChanged(ViewerPane pane)
    {
        _ui.BindActivePane(pane);
        HandleSessionChanged(pane);
    }

    private void HandleLayoutChanged(WorkspaceSplit? openingSplit)
    {
        _ui.ApplyLayout(_workspace.Root, openingSplit);
        _window.RequestRepaint();
    }

    private void HandlePaneRatiosChanged()
    {
        _ui.ApplyPaneRatios(_workspace.Root);
        _window.RequestRepaint();
    }

    private void CloseTabOrPane(ViewerPane pane, int index)
    {
        if (pane.Count > 1)
        {
            _workspace.CloseTab(pane, index);
            return;
        }

        if (_ui.BeginClosePane(pane, () => _workspace.RemovePane(pane)))
        {
            _workspace.ActivatePaneAfterClosing(pane);
            _window.RequestRepaint();
            return;
        }

        _window.Close();
    }

    private void HandleTabsChanged(ViewerPane pane)
    {
        _ui.ApplyTabs(
            pane,
            pane.Tabs
                .Select(tab => new ViewerTabInfo(
                    Path.GetFileName(tab.Session.State.RequestedPath) ?? "New tab",
                    tab.Session.State.RequestedPath))
                .ToArray(),
            pane.ActiveIndex);
    }

    private ViewerTab CreateTab()
    {
        ImageLoadClient loadClient = _imageLoadService.CreateClient();
        var imageLoader = new PresentationImageLoader(
            loadClient,
            _renderBitmapCache,
            _renderer.DeviceContext,
            _thumbnailImageLoader,
            _imageInfoLoader,
            _uiContext);
        var folderMonitor = new FolderMonitor(
            _folderScanner,
            new FileSystemFolderWatcher(),
            _uiContext);
        var session = new ViewerSession(
            new FolderNavigator(),
            folderMonitor,
            imageLoader);
        return new ViewerTab(session);
    }

    private void HandleDpiChanged(float dpi)
    {
        _renderer.SetDpi(dpi);
        _ui.SetDpi(dpi);
    }

    private void HandleResize(int width, int height)
    {
        _renderer.Resize(width, height);
        _window.RequestRepaint();
    }

    private void HandlePointerInput(WindowPointerEvent input)
    {
        if (input.Kind != WindowPointerEventKind.Cancelled)
        {
            _pointerX = (int)input.Position.X;
            _pointerY = (int)input.Position.Y;
        }

        SendPointerEvent(input);
    }

    private void HandleRenderFrame()
    {
        bool animationContinues = _ui.Update();
        _renderer.Render(_ui.DrawFrame, _ui.Palette.Background);

        if (animationContinues)
        {
            _window.RequestRepaint();
        }
        else if (_ui.NextAnimationFrameDelay is { } delay)
        {
            _window.RequestRepaintAfter(delay);
        }
    }

    private void SendPointerEvent(WindowPointerEvent input)
    {
        _ui.HandlePointer(input);
    }
}

