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
    private readonly AppWindow _window;
    private readonly D2DRenderer _renderer;
    private readonly ViewerUi _ui;
    private readonly ImageLoadCoordinator _imageLoadCoordinator;
    private readonly ViewerSession _session;
    private readonly SettingsService _settings;
    private int _pointerX;
    private int _pointerY;

    public DameviewApp()
    {
        _window = new AppWindow("Dameview", 1100, 720);
        _window.SetTitleBarTheme(dark: true);
        _pointerX = _window.ClientWidth / 2;
        _pointerY = _window.ClientHeight / 2;
        _renderer = new D2DRenderer(
            _window.Handle,
            _window.ClientWidth,
            _window.ClientHeight,
            _window.Dpi);
        _imageLoadCoordinator = new ImageLoadCoordinator(
            _window.Post,
            new WindowsImageLoadingBackend());
        using var imageDecoder = new ImageDecoder();
        HashSet<string> extensions = imageDecoder.GetProbablySupportedExtensions();
        _session = new ViewerSession(
            new FolderNavigator(),
            _imageLoadCoordinator,
            new FolderScanner(path => extensions.Contains(Path.GetExtension(path))),
            _window.Post,
            _window.ClientWidth,
            _window.ClientHeight);
        _settings = new SettingsService(SettingsService.DefaultPath, _window.Post);
        _ui = new ViewerUi(
            _renderer.DeviceContext,
            _renderer.DirectWriteFactory,
            _session,
            _window.Dpi,
            UiTheme.Default,
            this,
            theme => _settings!.Update(_settings.Current with { Theme = theme }),
            sort => _settings!.Update(_settings.Current with { Sort = sort }));
        _session.StateChanged += HandleSessionChanged;

        _window.RenderFrame += HandleRenderFrame;
        _window.Resized += HandleResize;
        _window.DpiChanged += HandleDpiChanged;
        _window.FileDropped += _session.OpenImage;
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
            _session.OpenImage(imagePath);
        }

        return _window.Run(_renderer.FrameLatencyWaitHandle);
    }

    public void Dispose()
    {
        // WINDOWPLACEMENT keeps rcNormalPosition up to date while maximized,
        // so this also remembers the size that will be restored after unmaximizing.
        _settings.Update(_settings.Current with { Window = _window.CapturePlacement() });
        _settings.Dispose();
        _ui.Dispose();
        _session.Dispose();
        _imageLoadCoordinator.Dispose();
        _renderer.Dispose();
        _window.Dispose();
    }

    public void ShowPreviousImage()
    {
        _session.ShowPreviousImage();
    }

    public void ShowNextImage()
    {
        _session.ShowNextImage();
    }

    public void FitImage()
    {
        if (_session.Animator.Fit())
        {
            _window.RequestRepaint();
        }
    }

    private void ShowActualSize(PointF anchor)
    {
        if (_session.Animator.ShowActualSizeAt(anchor.X, anchor.Y))
        {
            _window.RequestRepaint();
        }
    }

    public void ShowActualSize()
    {
        ShowActualSize(_session!.Viewport.ViewportCenter);
    }

    private void HandleKeyPress(UiKeyEvent input)
    {
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
                ShowActualSize(new PointF(_pointerX, _pointerY));
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
            UiTheme theme = current.Theme == ThemeMode.Light ? UiTheme.Light : UiTheme.Default;
            _ui.Palette = theme;
            _window.SetTitleBarTheme(current.Theme == ThemeMode.Dark);
        }

        if (previous.Sort != current.Sort)
        {
            _session.SetSort(current.Sort);
        }

        _window.RequestRepaint();
    }

    private void HandleSessionChanged()
    {
        ViewerSessionState state = _session.State;
        string fileName = state.RequestedPath is null
            ? "Dameview"
            : Path.GetFileName(state.RequestedPath);
        _window.SetTitle($"{fileName} — Dameview");
        _ui.ApplyState(state);
        _window.RequestRepaint();
    }

    private void HandleDpiChanged(float dpi)
    {
        _renderer.SetDpi(dpi);
        _ui.SetDpi(dpi);
    }

    private void HandleResize(int width, int height)
    {
        _session.SetViewportSize(width, height);
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
        var size = new SizeF(_window.ClientWidth, _window.ClientHeight);
        if (_ui.HandlePointer(input, size).NeedsRepaint)
        {
            _window.RequestRepaint();
        }
    }
}

