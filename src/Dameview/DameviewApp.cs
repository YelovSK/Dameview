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
    private readonly ImageDecoder _imageDecoder = new();
    private SettingsService? _settings;
    private AppWindow? _window;
    private D2DRenderer? _renderer;
    private ViewerUi? _ui;
    private Action<SizeF>? _drawUi;
    private ImageLoadCoordinator? _imageLoadCoordinator;
    private ViewerSession? _session;
    private int _pointerX;
    private int _pointerY;

    public int Run(string[] args)
    {
        _window = new AppWindow("Dameview", 1100, 720);
        _pointerX = _window.ClientWidth / 2;
        _pointerY = _window.ClientHeight / 2;
        _renderer = new D2DRenderer(
            _window.Handle,
            _window.ClientWidth,
            _window.ClientHeight,
            _window.Dpi);
        _imageLoadCoordinator = new ImageLoadCoordinator(_window.Post);
        HashSet<string> extensions = _imageDecoder.GetProbablySupportedExtensions();
        _session = new ViewerSession(
            new FolderNavigator(),
            _imageLoadCoordinator,
            new FolderScanner(path => extensions.Contains(Path.GetExtension(path))),
            _window.Post,
            _window.ClientWidth,
            _window.ClientHeight);
        _ui = new ViewerUi(
            _renderer.DeviceContext,
            _renderer.DirectWriteFactory,
            _session,
            _window.Dpi,
            UiTheme.Default,
            this);
        _drawUi = _ui.DrawFrame;
        _session.StateChanged += HandleSessionChanged;

        _window.RenderFrame += HandleRenderFrame;
        _window.Resized += HandleResize;
        _window.DpiChanged += HandleDpiChanged;
        _window.FileDropped += _session.OpenImage;
        _window.KeyPressed += HandleKeyPress;
        _window.PointerPressed += HandlePointerPressed;
        _window.PointerMoved += HandlePointerMoved;
        _window.PointerReleased += HandlePointerReleased;
        _window.PointerCancelled += HandlePointerCancelled;
        _window.PointerDoubleClicked += HandlePointerDoubleClick;
        _window.MouseWheel += HandleMouseWheel;

        _settings = new SettingsService(SettingsService.DefaultPath, _window.Post);
        _settings.Changed += ApplySettings;
        _settings.ErrorChanged += () =>
        {
            _ui.SettingsError = _settings.Error;
            _window.RequestRepaint();
        };
        _settings.Start();

        if (args.FirstOrDefault() is string imagePath)
        {
            _session.OpenImage(imagePath);
        }

        return _window.Run(_renderer.FrameLatencyWaitHandle);
    }

    public void Dispose()
    {
        _settings?.Dispose();
        _session?.Dispose();
        _imageLoadCoordinator?.Dispose();
        _ui?.Dispose();
        _renderer?.Dispose();
        _window?.Dispose();
        _imageDecoder.Dispose();
    }

    public void ShowPreviousImage()
    {
        _session!.ShowPreviousImage();
    }

    public void ShowNextImage()
    {
        _session!.ShowNextImage();
    }

    public void FitImage()
    {
        if (_session!.Animator.Fit())
        {
            _window!.RequestRepaint();
        }
    }

    private void ShowActualSize(PointF anchor)
    {
        if (_session!.Animator.ShowActualSizeAt(anchor.X, anchor.Y))
        {
            _window!.RequestRepaint();
        }
    }

    public void ShowActualSize()
    {
        ShowActualSize(_session!.Viewport.ViewportCenter);
    }

    private void HandleKeyPress(uint key)
    {
        switch (key)
        {
            case NativeMethods.VirtualKeyLeft:
                ShowPreviousImage();
                break;

            case NativeMethods.VirtualKeyRight:
                ShowNextImage();
                break;

            case NativeMethods.VirtualKeyF:
                FitImage();
                break;

            case NativeMethods.VirtualKey1:
            case NativeMethods.VirtualKeyNumpad1:
                ShowActualSize(new PointF(_pointerX, _pointerY));
                break;
        }
    }

    private void ApplySettings(AppSettings previous, AppSettings current)
    {
        if (previous.Theme != current.Theme)
        {
            UiTheme theme = current.Theme == ThemeMode.Light ? UiTheme.Light : UiTheme.Default;
            _ui!.Palette = theme;
        }

        if (previous.Sort != current.Sort)
        {
            _session!.SetSort(current.Sort);
        }

        _window!.RequestRepaint();
    }

    private void HandleSessionChanged()
    {
        _ui!.ApplyState(_session!.State);
        _window!.RequestRepaint();
    }

    private void HandleDpiChanged(float dpi)
    {
        _renderer!.SetDpi(dpi);
        _ui!.SetDpi(dpi);
    }

    private void HandleResize(int width, int height)
    {
        _session!.SetViewportSize(width, height);
        _renderer!.Resize(width, height);
        _window!.RequestRepaint();
    }

    private void HandlePointerPressed(int x, int y)
    {
        _pointerX = x;
        _pointerY = y;
        SendPointerEvent(new UiPointerEvent(
            UiPointerEventKind.Pressed,
            new PointF(x, y),
            PointerButton.Primary));
    }

    private void HandlePointerMoved(int x, int y)
    {
        _pointerX = x;
        _pointerY = y;
        SendPointerEvent(new UiPointerEvent(
            UiPointerEventKind.Moved,
            new PointF(x, y)));
    }

    private void HandlePointerReleased(int x, int y)
    {
        _pointerX = x;
        _pointerY = y;
        SendPointerEvent(new UiPointerEvent(
            UiPointerEventKind.Released,
            new PointF(_pointerX, _pointerY),
            PointerButton.Primary));
    }

    private void HandlePointerCancelled()
    {
        SendPointerEvent(new UiPointerEvent(UiPointerEventKind.Cancelled, PointF.Empty));
    }

    private void HandlePointerDoubleClick(int x, int y)
    {
        _pointerX = x;
        _pointerY = y;
        SendPointerEvent(new UiPointerEvent(
            UiPointerEventKind.DoubleClicked,
            new PointF(x, y),
            PointerButton.Primary));
    }

    private void HandleMouseWheel(int x, int y, int delta)
    {
        _pointerX = x;
        _pointerY = y;
        SendPointerEvent(new UiPointerEvent(
            UiPointerEventKind.Wheel,
            new PointF(x, y),
            WheelDelta: delta));
    }

    private void HandleRenderFrame()
    {
        bool animationContinues = _ui!.Update();
        _renderer!.Render(_drawUi!, _ui.Palette.Background);

        if (animationContinues)
        {
            _window!.RequestRepaint();
        }
    }

    private void SendPointerEvent(UiPointerEvent input)
    {
        var size = new SizeF(_window!.ClientWidth, _window.ClientHeight);
        if (_ui!.HandlePointer(input, size).NeedsRepaint)
        {
            _window.RequestRepaint();
        }
    }
}

