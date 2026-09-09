using System.Drawing;
using Dameview.Commands;
using Dameview.Imaging;
using Dameview.Navigation;
using Dameview.Platform;
using Dameview.Rendering;
using Dameview.Settings;
using Dameview.UI;
using Dameview.Viewing;

namespace Dameview;

internal sealed class DameviewApp : IViewerCommands, IDisposable
{
    private const long RenderBitmapCacheCapacityBytes = 256L * 1024L * 1024L;

    private readonly AppWindow _window;
    private readonly D2DRenderer _renderer;
    private readonly ViewerUi _ui;
    private readonly WindowsImageLoadingBackend _imageBackend;
    private readonly ThumbnailCoordinator _thumbnailCoordinator;
    private readonly ImageLoadService _imageLoadService;
    private readonly RenderBitmapCache _renderBitmapCache;
    private readonly IFolderScanner _folderScanner;
    private readonly ViewerWorkspace _workspace;
    private readonly SettingsService _settings;
    private int _pointerX;
    private int _pointerY;

    public DameviewApp()
    {
        _window = new AppWindow("Dameview", 1100, 720);
        _window.SetTitleBarTheme(dark: true, UiTheme.Default.Background, UiTheme.Default.PrimaryText);
        _pointerX = _window.ClientWidth / 2;
        _pointerY = _window.ClientHeight / 2;
        _renderer = new D2DRenderer(
            _window.Handle,
            _window.ClientWidth,
            _window.ClientHeight,
            _window.Dpi);
        _imageBackend = new WindowsImageLoadingBackend();
        _thumbnailCoordinator = new ThumbnailCoordinator(_window.Post, _imageBackend.LoadThumbnail);
        _imageLoadService = new ImageLoadService(
            _window.Post,
            _imageBackend,
            new ImageRepresentationPolicy(checked((int)_renderer.DeviceContext.MaximumBitmapSize)),
            _thumbnailCoordinator);
        _renderBitmapCache = new RenderBitmapCache(RenderBitmapCacheCapacityBytes);
        using var imageDecoder = new ImageDecoder();
        HashSet<string> extensions = imageDecoder.GetProbablySupportedExtensions();
        _folderScanner = new FolderScanner(path => extensions.Contains(Path.GetExtension(path)));
        _workspace = new ViewerWorkspace(CreateTab);
        _settings = new SettingsService(SettingsService.DefaultPath, _window.Post);
        _ui = new ViewerUi(
            _renderer.DeviceContext,
            _renderer.DirectWriteFactory,
            _workspace.ActivePane,
            _window.Dpi,
            UiTheme.Default,
            this,
            _thumbnailCoordinator,
            theme => _settings!.Update(_settings.Current with { Theme = theme }),
            sort => _settings!.Update(_settings.Current with { Sort = sort }),
            postToUi: _window.Post);
        _ui.Invalidated += _window.RequestRepaint;
        _ui.CursorChanged += _window.ApplyCursor;
        _workspace.ActivePaneChanged += HandleActivePaneChanged;
        _workspace.PaneActiveTabChanged += HandleActiveTabChanged;
        _workspace.PaneSessionStateChanged += HandleSessionChanged;
        _workspace.PaneTabsChanged += HandleTabsChanged;
        HandleTabsChanged(_workspace.ActivePane);

        _window.RenderFrame += HandleRenderFrame;
        _window.Resized += HandleResize;
        _window.DpiChanged += HandleDpiChanged;
        _window.FileDropped += _workspace.OpenImage;
        _window.KeyPressed += HandleKeyPress;
        _window.PointerInput += HandlePointerInput;

        _settings.Changed += ApplySettings;
        _settings.ErrorChanged += () =>
        {
            _ui.SettingsError = _settings.Error;
            _window.RequestRepaint();
        };
    }

    public int Run(string[] args)
    {
        _settings.Start();

        if (args.FirstOrDefault() is string imagePath)
        {
            _workspace.OpenImage(imagePath);
        }

        return _window.Run(_renderer.FrameLatencyWaitHandle);
    }

    public void Dispose()
    {
        // WINDOWPLACEMENT keeps rcNormalPosition up to date while maximized,
        // so this also remembers the size that will be restored after unmaximizing.
        _settings.Update(_settings.Current with { Window = _window.CapturePlacement() });
        _settings.Dispose();
        _ui.Invalidated -= _window.RequestRepaint;
        _ui.Dispose();
        _workspace.Dispose();
        _renderBitmapCache.Dispose();
        _imageLoadService.Dispose();
        _imageBackend.Dispose();
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

    public void OpenImage(string path)
    {
        _workspace.SelectImage(path);
    }

    public void OpenImageInNewTab(string path)
    {
        _workspace.OpenImageInNewTab(path);
    }

    public void SelectTab(ViewerPane pane, int index)
    {
        _workspace.SelectTab(pane, index);
    }

    public void CloseTab(ViewerPane pane, int index)
    {
        if (!_workspace.CloseTab(pane, index))
        {
            _window.Close();
        }
    }

    private void HandleKeyPress(UiKeyEvent input)
    {
        if (input.Control && input.Key == UiKey.W)
        {
            if (!_workspace.CloseActiveTab())
            {
                _window.Close();
            }

            return;
        }

        if (input.Control && input.Key == UiKey.Tab)
        {
            _workspace.SelectRelativeTab(input.Shift ? -1 : 1);
            return;
        }

        if (_ui.HandleKey(input))
        {
            _window.RequestRepaint();
            return;
        }

        switch (input.Key)
        {
            case UiKey.Left:
                ShowPreviousImage();
                break;

            case UiKey.Right:
                ShowNextImage();
                break;

            case UiKey.F:
                FitImage();
                break;

            case UiKey.Number1:
            case UiKey.Numpad1:
                ShowActualSize(_ui.GetImageViewportPoint(new PointF(_pointerX, _pointerY)));
                break;
        }
    }

    private void ApplySettings(AppSettings previous, AppSettings current)
    {
        if (current.Window is { IsUsable: true } windowPlacement && previous.Window != windowPlacement)
        {
            _window.RestorePlacement(windowPlacement);
        }

        _ui.ApplySettings(current);
        if (previous.Theme != current.Theme)
        {
            _ui.Palette = current.Theme.Palette;
            _window.SetTitleBarTheme(current.Theme.IsDark, current.Theme.Palette.Background, current.Theme.Palette.PrimaryText);
        }

        if (previous.Sort != current.Sort)
        {
            _workspace.SetSort(current.Sort);
        }

        _window.RequestRepaint();
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
            _renderer.DeviceContext);
        var folderMonitor = new FolderMonitor(
            _folderScanner,
            new FileSystemFolderWatcher(),
            _window.Post);
        var session = new ViewerSession(
            new FolderNavigator(),
            folderMonitor,
            imageLoader);
        return new ViewerTab(
            session,
            folderMonitor,
            imageLoader,
            loadClient);
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

    private void HandlePointerInput(UiPointerEvent input)
    {
        if (input.Kind != UiPointerEventKind.Cancelled)
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

    private void SendPointerEvent(UiPointerEvent input)
    {
        _ui.HandlePointer(input);
    }
}

