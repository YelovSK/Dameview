using Dameview.UI;
using Microsoft.Win32.SafeHandles;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
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
    private readonly UiTheme _theme;
    private ID2D1Bitmap1? _targetBitmap;
    private int _width;
    private int _height;
    private float _dpi;

    internal D2DRenderer(
        nint window,
        int width,
        int height,
        float dpi,
        UiTheme theme)
    {
        _width = width;
        _height = height;
        _dpi = dpi;
        _theme = theme;

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
    }

    internal nint FrameLatencyWaitHandle => _frameLatencyWaitHandle.DangerousGetHandle();
    internal ID2D1DeviceContext DeviceContext => _deviceContext;
    internal IDWriteFactory DirectWriteFactory => _directWriteFactory;

    internal void Render(IUiElement content)
    {
        if (_width <= 0 || _height <= 0)
        {
            return;
        }

        _deviceContext.BeginDraw();
        _deviceContext.Clear(_theme.Background);
        var drawContext = new UiDrawContext(_deviceContext, _dpi);
        content.Draw(drawContext, new System.Drawing.SizeF(_width, _height));

        _deviceContext.EndDraw().CheckError();
        _swapChain.Present(1, PresentFlags.None).CheckError();
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

}
