using System.Diagnostics;
using System.Drawing;
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

internal readonly record struct RenderTiming(TimeSpan? GpuTime, long SubmissionCompleted);

internal sealed class D2DRenderer : IDisposable
{
    private const uint BufferCount = 2;

    private readonly ID3D11Device _d3dDevice;
    private readonly ID3D11DeviceContext _d3dContext;
    private readonly GpuFrameTimer _gpuFrameTimer;
    private readonly IDXGIDevice _dxgiDevice;
    private readonly IDXGIFactory2 _dxgiFactory;
    private readonly IDXGISwapChain2 _swapChain;
    private readonly SafeWaitHandle _frameLatencyWaitHandle;
    private readonly ID2D1Factory1 _d2dFactory;
    private readonly ID2D1Device _d2dDevice;
    private readonly IDWriteFactory1 _directWriteFactory;
    private ID2D1Bitmap1? _targetBitmap;
    private ID3D11RenderTargetView? _targetView;
    private int _width;
    private int _height;
    private float _dpi;

    internal D2DRenderer(
        nint window,
        int width,
        int height,
        float dpi)
    {
        _width = width;
        _height = height;
        _dpi = dpi;

        _d3dDevice = D3D11CreateDevice(
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            Vortice.Direct3D.FeatureLevel.Level_11_1,
            Vortice.Direct3D.FeatureLevel.Level_11_0,
            Vortice.Direct3D.FeatureLevel.Level_10_1,
            Vortice.Direct3D.FeatureLevel.Level_10_0);
        _dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
        _d3dContext = _d3dDevice.ImmediateContext;
        _gpuFrameTimer = new GpuFrameTimer(_d3dDevice, _d3dContext);
        using IDXGIAdapter adapter = _dxgiDevice.GetAdapter();
        _dxgiFactory = adapter.GetParent<IDXGIFactory2>();

        _d2dFactory = D2D1CreateFactory<ID2D1Factory1>();
        _d2dDevice = _d2dFactory.CreateDevice(_dxgiDevice);
        DeviceContext = _d2dDevice.CreateDeviceContext();
        DeviceContext.SetDpi(dpi, dpi);
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
    internal ID2D1DeviceContext DeviceContext { get; }
    internal IDWriteFactory DirectWriteFactory => _directWriteFactory;

    internal RenderTiming Render(Action<SizeF> draw, Color4 background, bool measureGpu = false)
    {
        if (_width <= 0 || _height <= 0)
        {
            return new RenderTiming(null, Stopwatch.GetTimestamp());
        }

        // Clear before timing: the first backbuffer write can wait for presentation.
        // Match the premultiplied Direct2D target without splitting its draw batch.
        _d3dContext.ClearRenderTargetView(_targetView!, new Color4(
            background.R * background.A,
            background.G * background.A,
            background.B * background.A,
            background.A));
        TimeSpan? gpuTime = measureGpu ? _gpuFrameTimer.BeginFrame() : null;
        DeviceContext.BeginDraw();
        draw(new SizeF(_width, _height));

        DeviceContext.EndDraw().CheckError();
        if (measureGpu)
        {
            _gpuFrameTimer.EndFrame();
        }

        long submissionCompleted = Stopwatch.GetTimestamp();
        _swapChain.Present(1, PresentFlags.None).CheckError();
        return new RenderTiming(gpuTime, submissionCompleted);
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
        DeviceContext.SetDpi(dpi, dpi);

        if (_width > 0 && _height > 0)
        {
            ReleaseTargetBitmap();
            CreateTargetBitmap();
        }
    }

    public void Dispose()
    {
        ReleaseTargetBitmap();
        DeviceContext.Dispose();
        _d2dDevice.Dispose();
        _directWriteFactory.Dispose();
        _d2dFactory.Dispose();
        _frameLatencyWaitHandle.Dispose();
        _swapChain.Dispose();
        _dxgiFactory.Dispose();
        _dxgiDevice.Dispose();
        _gpuFrameTimer.Dispose();
        _d3dContext.Dispose();
        _d3dDevice.Dispose();
    }

    private void CreateTargetBitmap()
    {
        using IDXGISurface surface = _swapChain.GetBuffer<IDXGISurface>(0);
        using ID3D11Texture2D texture = surface.QueryInterface<ID3D11Texture2D>();
        _targetView = _d3dDevice.CreateRenderTargetView(texture);
        BitmapProperties1 properties = new(
            new PixelFormat(
                Format.B8G8R8A8_UNorm,
                Vortice.DCommon.AlphaMode.Premultiplied),
            _dpi,
            _dpi,
            BitmapOptions.Target | BitmapOptions.CannotDraw);

        _targetBitmap = DeviceContext.CreateBitmapFromDxgiSurface(surface, properties);
        DeviceContext.Target = _targetBitmap;
    }

    private void ReleaseTargetBitmap()
    {
        DeviceContext.Target = null;
        _targetBitmap?.Dispose();
        _targetBitmap = null;
        _targetView?.Dispose();
        _targetView = null;
    }

}
