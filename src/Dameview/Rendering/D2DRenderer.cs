using Dameview.Imaging;
using Dameview.UI;
using Dameview.Viewing;
using Microsoft.Win32.SafeHandles;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct2D1.D2D1;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DirectWrite.DWrite;

namespace Dameview.Rendering;

internal sealed class D2DRenderer : IDisposable
{
    private const uint BufferCount = 2;

    private readonly ID3D11Device _d3dDevice;
    private readonly IDXGIDevice _dxgiDevice;
    private readonly IDXGIFactory2 _dxgiFactory;
    private readonly IDXGISwapChain2 _swapChain;
    private readonly SafeWaitHandle _frameLatencyWaitHandle;
    private readonly ID2D1Factory1 _d2dFactory;
    private readonly ID2D1Device _d2dDevice;
    private readonly ID2D1DeviceContext _deviceContext;
    private readonly IDWriteFactory1 _directWriteFactory;
    private readonly AppUi _ui;
    private readonly ImageViewport _viewport;
    private ID2D1Bitmap1? _targetBitmap;
    private ID2D1Bitmap1? _image;
    private int _width;
    private int _height;
    private float _dpi;

    internal D2DRenderer(nint window, int width, int height, float dpi, ImageViewport viewport)
    {
        _width = width;
        _height = height;
        _dpi = dpi;
        _viewport = viewport;

        _d3dDevice = D3D11CreateDevice(
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            Vortice.Direct3D.FeatureLevel.Level_11_1,
            Vortice.Direct3D.FeatureLevel.Level_11_0,
            Vortice.Direct3D.FeatureLevel.Level_10_1,
            Vortice.Direct3D.FeatureLevel.Level_10_0);
        _dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = _dxgiDevice.GetAdapter();
        _dxgiFactory = adapter.GetParent<IDXGIFactory2>();

        _d2dFactory = D2D1CreateFactory<ID2D1Factory1>();
        _d2dDevice = _d2dFactory.CreateDevice(_dxgiDevice);
        _deviceContext = _d2dDevice.CreateDeviceContext();
        _deviceContext.SetDpi(dpi, dpi);
        _directWriteFactory = DWriteCreateFactory<IDWriteFactory1>();

        SwapChainDescription1 description = new(
            (uint)Math.Max(width, 1),
            (uint)Math.Max(height, 1),
            Format.B8G8R8A8_UNorm,
            false,
            Usage.RenderTargetOutput,
            BufferCount,
            Scaling.Stretch,
            SwapEffect.FlipSequential,
            Vortice.DXGI.AlphaMode.Ignore,
            SwapChainFlags.FrameLatencyWaitableObject);

        using IDXGISwapChain1 swapChain = _dxgiFactory.CreateSwapChainForHwnd(
            _d3dDevice,
            window,
            description);
        _swapChain = swapChain.QueryInterface<IDXGISwapChain2>();
        _swapChain.MaximumFrameLatency = 1;
        _frameLatencyWaitHandle = new SafeWaitHandle(
            _swapChain.FrameLatencyWaitableObject,
            ownsHandle: true);

        CreateTargetBitmap();
        _ui = new AppUi(_deviceContext, _directWriteFactory);
    }

    internal nint FrameLatencyWaitHandle => _frameLatencyWaitHandle.DangerousGetHandle();

    internal void Render()
    {
        if (_width <= 0 || _height <= 0)
        {
            return;
        }

        _deviceContext.BeginDraw();
        _deviceContext.Clear(Theme.Background);

        float width = PixelsToDips(_width);
        float height = PixelsToDips(_height);
        if (_image is null)
        {
            _ui.Draw(_deviceContext, width, height);
        }
        else
        {
            DrawImage();
        }

        _deviceContext.EndDraw().CheckError();
        _swapChain.Present(1, PresentFlags.None).CheckError();
    }

    internal unsafe void SetImage(DecodedImage image)
    {
        BitmapProperties1 properties = new(
            new PixelFormat(
                Format.B8G8R8A8_UNorm,
                Vortice.DCommon.AlphaMode.Premultiplied),
            96.0f,
            96.0f,
            BitmapOptions.None);

        ID2D1Bitmap1 newImage;
        fixed (byte* pixels = image.Pixels)
        {
            newImage = _deviceContext.CreateBitmap(
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

        if (width <= 0 || height <= 0)
        {
            return;
        }

        ReleaseTargetBitmap();
        _swapChain.ResizeBuffers(
            BufferCount,
            (uint)width,
            (uint)height,
            Format.B8G8R8A8_UNorm,
            SwapChainFlags.FrameLatencyWaitableObject).CheckError();
        CreateTargetBitmap();
    }

    internal void SetDpi(float dpi)
    {
        _dpi = dpi;
        _deviceContext.SetDpi(dpi, dpi);

        if (_width > 0 && _height > 0)
        {
            ReleaseTargetBitmap();
            CreateTargetBitmap();
        }
    }

    public void Dispose()
    {
        _deviceContext.Target = null;
        _image?.Dispose();
        _ui.Dispose();
        _targetBitmap?.Dispose();
        _deviceContext.Dispose();
        _d2dDevice.Dispose();
        _directWriteFactory.Dispose();
        _d2dFactory.Dispose();
        _frameLatencyWaitHandle.Dispose();
        _swapChain.Dispose();
        _dxgiFactory.Dispose();
        _dxgiDevice.Dispose();
        _d3dDevice.Dispose();
    }

    private void CreateTargetBitmap()
    {
        using IDXGISurface surface = _swapChain.GetBuffer<IDXGISurface>(0);
        BitmapProperties1 properties = new(
            new PixelFormat(
                Format.B8G8R8A8_UNorm,
                Vortice.DCommon.AlphaMode.Premultiplied),
            _dpi,
            _dpi,
            BitmapOptions.Target | BitmapOptions.CannotDraw);

        _targetBitmap = _deviceContext.CreateBitmapFromDxgiSurface(surface, properties);
        _deviceContext.Target = _targetBitmap;
    }

    private void ReleaseTargetBitmap()
    {
        _deviceContext.Target = null;
        _targetBitmap?.Dispose();
        _targetBitmap = null;
    }

    private float PixelsToDips(float pixels)
    {
        return pixels * 96.0f / _dpi;
    }

    private void DrawImage()
    {
        ID2D1Bitmap1 image = _image!;
        System.Drawing.RectangleF destination = _viewport.GetDestinationRectangle();

        _deviceContext.DrawBitmap(
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
