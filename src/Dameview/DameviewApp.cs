using Dameview.Imaging;
using Dameview.Navigation;
using Dameview.Platform;
using Dameview.Rendering;

namespace Dameview;

internal sealed class DameviewApp : IDisposable
{
    private readonly ImageDecoder _imageDecoder = new();
    private readonly FolderNavigator _folderNavigator;
    private AppWindow? _window;
    private D2DRenderer? _renderer;

    internal DameviewApp()
    {
        _folderNavigator = new FolderNavigator(
            _imageDecoder.IsProbablySupported,
            FolderSort.NameAscending);
    }

    public int Run(string[] args)
    {
        _window = new AppWindow("Dameview", 1100, 720);
        _renderer = new D2DRenderer(
            _window.Handle,
            _window.ClientWidth,
            _window.ClientHeight,
            _window.Dpi);

        _window.Paint += _renderer.Render;
        _window.Resized += _renderer.Resize;
        _window.DpiChanged += _renderer.SetDpi;
        _window.FileDropped += OpenImage;
        _window.KeyPressed += HandleKeyPress;

        if (args.FirstOrDefault() is string imagePath)
        {
            OpenImage(imagePath);
        }

        return _window.Run();
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
    }
}

