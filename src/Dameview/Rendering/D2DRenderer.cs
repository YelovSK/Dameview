using Dameview.Imaging;
using Dameview.UI;
using Dameview.Viewing;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct2D1.D2D1;
using static Vortice.DirectWrite.DWrite;

namespace Dameview.Rendering;

internal sealed class D2DRenderer : IDisposable
{
    private readonly nint _window;
    private readonly ID2D1Factory1 _d2dFactory;
    private readonly IDWriteFactory1 _directWriteFactory;
    private readonly ID2D1HwndRenderTarget _renderTarget;
    private readonly AppUi _ui;
    private readonly ImageViewport _viewport;
    private ID2D1Bitmap? _image;
    private int _width;
    private int _height;
    private float _dpi;

    internal D2DRenderer(nint window, int width, int height, float dpi, ImageViewport viewport)
    {
        _window = window;
        _width = width;
        _height = height;
        _dpi = dpi;
        _viewport = viewport;

        _d2dFactory = D2D1CreateFactory<ID2D1Factory1>();
        _directWriteFactory = DWriteCreateFactory<IDWriteFactory1>();
        (_renderTarget, _ui) = CreateDeviceResources();
    }

    internal void Render()
    {
        _renderTarget.BeginDraw();
        _renderTarget.Clear(Theme.Background);

        float width = PixelsToDips(_width);
        float height = PixelsToDips(_height);
        if (_image is null)
        {
            _ui.Draw(_renderTarget, width, height);
        }
        else
        {
            DrawImage();
        }

        _renderTarget.EndDraw();
    }

    internal unsafe void SetImage(DecodedImage image)
    {
        BitmapProperties properties = new(
            new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied));

        ID2D1Bitmap newImage;
        fixed (byte* pixels = image.Pixels)
        {
            newImage = _renderTarget.CreateBitmap(
                new SizeI(image.Width, image.Height),
                (nint)pixels,
                (uint)image.Stride,
                properties);
        }

        _image?.Dispose();
        _image = newImage;
    }

    internal void Resize(int width, int height)
    {
        _width = width;
        _height = height;

        if (width > 0 && height > 0)
        {
            _renderTarget.Resize(new SizeI(width, height));
        }
    }

    internal void SetDpi(float dpi)
    {
        _dpi = dpi;
        _renderTarget.SetDpi(dpi, dpi);
    }

    public void Dispose()
    {
        _image?.Dispose();
        _ui.Dispose();
        _renderTarget.Dispose();
        _directWriteFactory.Dispose();
        _d2dFactory.Dispose();
    }

    private (ID2D1HwndRenderTarget RenderTarget, AppUi Ui) CreateDeviceResources()
    {
        ID2D1HwndRenderTarget renderTarget = _d2dFactory.CreateHwndRenderTarget(
            new RenderTargetProperties(),
            new HwndRenderTargetProperties
            {
                Hwnd = _window,
                PixelSize = new SizeI(_width, _height),
            });

        renderTarget.SetDpi(_dpi, _dpi);
        return (renderTarget, new AppUi(renderTarget, _directWriteFactory));
    }

    private float PixelsToDips(float pixels)
    {
        return pixels * 96.0f / _dpi;
    }

    private void DrawImage()
    {
        ID2D1Bitmap image = _image!;
        System.Drawing.RectangleF destination = _viewport.GetDestinationRectangle();

        _renderTarget.DrawBitmap(
            image,
            new Rect(
                PixelsToDips(destination.X),
                PixelsToDips(destination.Y),
                PixelsToDips(destination.Width),
                PixelsToDips(destination.Height)),
            1.0f,
            BitmapInterpolationMode.Linear,
            new Rect(0.0f, 0.0f, image.PixelSize.Width, image.PixelSize.Height));
    }
}
