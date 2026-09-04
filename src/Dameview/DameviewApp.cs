using Dameview.Imaging;
using Dameview.Navigation;
using Dameview.Platform;
using Dameview.Rendering;
using Dameview.Viewing;

namespace Dameview;

internal sealed class DameviewApp : IDisposable
{
    private readonly ImageDecoder _imageDecoder = new();
    private readonly FolderNavigator _folderNavigator;
    private AppWindow? _window;
    private D2DRenderer? _renderer;
    private ImageViewport? _viewport;
    private bool _isPanning;
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
        _viewport = new ImageViewport(_window.ClientWidth, _window.ClientHeight);
        _renderer = new D2DRenderer(
            _window.Handle,
            _window.ClientWidth,
            _window.ClientHeight,
            _window.Dpi,
            _viewport);

        _window.RenderFrame += _renderer.Render;
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

    private void OpenImage(string path)
    {
        DecodedImage image = _imageDecoder.Decode(path);
        _renderer!.SetImage(image);
        _viewport!.SetImageSize(image.Width, image.Height);
        _folderNavigator.SetCurrent(path);
        _window!.RequestRepaint();
    }

    private void HandleKeyPress(uint key)
    {
        string? path = key switch
        {
            NativeMethods.VirtualKeyLeft => _folderNavigator.GetPreviousPath(),
            NativeMethods.VirtualKeyRight => _folderNavigator.GetNextPath(),
            _ => null,
        };

        if (path is not null)
        {
            OpenImage(path);
        }

        switch (key)
        {
            case NativeMethods.VirtualKeyF:
                _viewport!.Fit();
                _window!.RequestRepaint();
                break;

            case NativeMethods.VirtualKey1:
            case NativeMethods.VirtualKeyNumpad1:
                _viewport!.ShowActualSize();
                _window!.RequestRepaint();
                break;
        }
    }

    private void HandleResize(int width, int height)
    {
        _viewport!.SetViewportSize(width, height);
        _renderer!.Resize(width, height);
        _window!.RequestRepaint();
    }

    private void HandlePointerPressed(int x, int y)
    {
        _isPanning = true;
        _pointerX = x;
        _pointerY = y;
    }

    private void HandlePointerMoved(int x, int y)
    {
        if (!_isPanning)
        {
            return;
        }

        _viewport!.PanBy(x - _pointerX, y - _pointerY);
        _pointerX = x;
        _pointerY = y;
        _window!.RequestRepaint();
    }

    private void HandlePointerReleased()
    {
        _isPanning = false;
    }

    private void HandlePointerDoubleClick()
    {
        _isPanning = false;
        _viewport!.ToggleFitAndActualSize();
        _window!.RequestRepaint();
    }

    private void HandleMouseWheel(int x, int y, int delta)
    {
        _viewport!.ZoomAt(x, y, delta);
        _window!.RequestRepaint();
    }
}

