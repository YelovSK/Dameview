using System.Drawing;
using Dameview.Commands;
using Dameview.Imaging;
using Dameview.Navigation;
using Dameview.Platform;
using Dameview.Rendering;
using Dameview.UI;
using SharpGen.Runtime;

namespace Dameview;

internal sealed class DameviewApp : IViewerCommands, IDisposable
{
    private readonly ImageDecoder _imageDecoder = new();
    private readonly FolderNavigator _folderNavigator;
    private readonly UiTheme _theme = UiTheme.Default;
    private AppWindow? _window;
    private D2DRenderer? _renderer;
    private ViewerUi? _ui;
    private int _pointerX;
    private int _pointerY;

    internal DameviewApp()
    {
        _folderNavigator = new FolderNavigator(
            _imageDecoder.IsProbablySupported,
            FolderSort.NameAscending);
    }

    public int Run(string[] args)
    {
        _window = new AppWindow("Dameview", 1100, 720);
        _pointerX = _window.ClientWidth / 2;
        _pointerY = _window.ClientHeight / 2;
        _renderer = new D2DRenderer(
            _window.Handle,
            _window.ClientWidth,
            _window.ClientHeight,
            _window.Dpi,
            _theme,
            this);
        _ui = _renderer.Ui;

        _window.RenderFrame += HandleRenderFrame;
        _window.Resized += HandleResize;
        _window.DpiChanged += _renderer.SetDpi;
        _window.FileDropped += OpenImage;
        _window.KeyPressed += HandleKeyPress;
        _window.PointerPressed += HandlePointerPressed;
        _window.PointerMoved += HandlePointerMoved;
        _window.PointerReleased += HandlePointerReleased;
        _window.PointerDoubleClicked += HandlePointerDoubleClick;
        _window.MouseWheel += HandleMouseWheel;

        if (args.FirstOrDefault() is string imagePath)
        {
            OpenImage(imagePath);
        }

        return _window.Run(_renderer.FrameLatencyWaitHandle);
    }

    public void Dispose()
    {
        _renderer?.Dispose();
        _window?.Dispose();
        _imageDecoder.Dispose();
    }

    public void ShowPreviousImage()
    {
        OpenImageIfAvailable(_folderNavigator.GetPreviousPath());
    }

    public void ShowNextImage()
    {
        OpenImageIfAvailable(_folderNavigator.GetNextPath());
    }

    public void FitImage()
    {
        if (_ui!.FitImage())
        {
            _window!.RequestRepaint();
        }
    }

    public void ShowActualSize(PointF anchor)
    {
        if (_ui!.ShowImageAtActualSize(anchor.X, anchor.Y))
        {
            _window!.RequestRepaint();
        }
    }

    public void ShowActualSize()
    {
        if (_ui!.ShowImageAtActualSize())
        {
            _window!.RequestRepaint();
        }
    }

    private void OpenImage(string path)
    {
        string? navigationError = null;

        try
        {
            _folderNavigator.SetCurrent(path);
        }
        catch (Exception exception) when (IsRecoverableImageError(exception))
        {
            navigationError = exception.Message;
        }

        try
        {
            DecodedImage image = _imageDecoder.Decode(path);
            _ui!.SetImage(image, path);

            if (navigationError is not null)
            {
                _ui.ShowError($"Image opened, but its folder could not be read: {navigationError}");
            }
        }
        catch (Exception exception) when (IsRecoverableImageError(exception))
        {
            _ui!.ShowError($"Could not open {Path.GetFileName(path)}: {exception.Message}");
        }

        _window!.RequestRepaint();
    }

    private static bool IsRecoverableImageError(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or OverflowException
            or SharpGenException;
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

    private void OpenImageIfAvailable(string? path)
    {
        if (path is not null)
        {
            OpenImage(path);
        }
    }

    private void HandleResize(int width, int height)
    {
        _ui!.SetViewportSize(width, height);
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

    private void HandlePointerReleased()
    {
        SendPointerEvent(new UiPointerEvent(
            UiPointerEventKind.Released,
            new PointF(_pointerX, _pointerY),
            PointerButton.Primary));
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
        _renderer!.Render();

        if (animationContinues)
        {
            _window!.RequestRepaint();
        }
    }

    private void SendPointerEvent(UiPointerEvent input)
    {
        var size = new SizeF(_window!.ClientWidth, _window.ClientHeight);
        if (_ui!.HandlePointer(input, size))
        {
            _window.RequestRepaint();
        }
    }
}

