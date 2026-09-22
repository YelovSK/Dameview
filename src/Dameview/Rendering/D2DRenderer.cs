using System.Diagnostics;
using Dameview.Diagnostics;
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

/// <param name="SubmitTime">Time spent in <c>EndDraw</c>.</param>
internal readonly record struct RenderTiming(
    TimeSpan? GpuTime,
    long SubmissionCompleted,
    TimeSpan SubmitTime = default);

/// <summary>
/// Owns the graphics device and the frame lifecycle. The Direct2D and DirectWrite factories
/// outlive any one device; everything built from the device is replaced together whenever the
/// device is, and <see cref="DeviceChanged"/> tells holders of device resources to rebuild.
/// </summary>
internal sealed class D2DRenderer : IDisposable
{
    private const uint BufferCount = 2;

    private readonly nint _window;
    private readonly ID2D1Factory1 _d2dFactory;
    private readonly IDWriteFactory1 _directWriteFactory;

    private ID3D11Device _d3dDevice;
    private ID3D11DeviceContext _d3dContext;
    private GpuFrameTimer _gpuFrameTimer;
    private IDXGIDevice _dxgiDevice;
    private IDXGIAdapter3? _videoMemoryAdapter;
    private IDXGIFactory2 _dxgiFactory;
    private IDXGISwapChain2 _swapChain;
    private SafeWaitHandle _frameLatencyWaitHandle;
    private ID2D1Device _d2dDevice;
    private ID2D1Bitmap1? _targetBitmap;
    private ID3D11RenderTargetView? _targetView;
    private int _width;
    private int _height;
    private float _dpi;

    internal D2DRenderer(
        nint window,
        int width,
        int height,
        float dpi,
        ID3D11Device device)
    {
        _window = window;
        _width = width;
        _height = height;
        _dpi = dpi;

        _d2dFactory = D2D1CreateFactory<ID2D1Factory1>();
        _directWriteFactory = DWriteCreateFactory<IDWriteFactory1>();
        StartupTrace.Mark("d2d-factories");

        // All assigned by CreateDeviceResources, which the compiler cannot see through.
        _d3dDevice = null!;
        _d3dContext = null!;
        _gpuFrameTimer = null!;
        _dxgiDevice = null!;
        _dxgiFactory = null!;
        _swapChain = null!;
        _frameLatencyWaitHandle = null!;
        _d2dDevice = null!;
        DeviceContext = null!;
        CreateDeviceResources(device);
    }

    /// <summary>Raised after the device was replaced, once the new resources are ready.</summary>
    internal event Action? DeviceChanged;

    internal nint FrameLatencyWaitHandle => _frameLatencyWaitHandle.DangerousGetHandle();
    internal ID2D1DeviceContext DeviceContext { get; private set; }
    internal IDWriteFactory DirectWriteFactory => _directWriteFactory;

    /// <summary>
    /// Creates a device off the window thread, so a slow hardware driver load does not block it.
    /// </summary>
    internal static ID3D11Device CreateDevice(DriverType driverType)
    {
        return D3D11CreateDevice(
            driverType,
            DeviceCreationFlags.BgraSupport,
            Vortice.Direct3D.FeatureLevel.Level_11_1,
            Vortice.Direct3D.FeatureLevel.Level_11_0,
            Vortice.Direct3D.FeatureLevel.Level_10_1,
            Vortice.Direct3D.FeatureLevel.Level_10_0);
    }

    /// <summary>
    /// Takes ownership of <paramref name="device"/> and rebuilds everything derived from the
    /// previous one. Holders of device resources must rebuild theirs from
    /// <see cref="DeviceContext"/> before the next frame, which is what
    /// <see cref="DeviceChanged"/> asks them to do.
    /// </summary>
    internal void AdoptDevice(ID3D11Device device)
    {
        ReleaseDeviceResources();
        CreateDeviceResources(device);
        DeviceChanged?.Invoke();
    }

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

        long recordingCompleted = Stopwatch.GetTimestamp();
        DeviceContext.EndDraw().CheckError();
        TimeSpan submitTime = Stopwatch.GetElapsedTime(recordingCompleted);
        if (measureGpu)
        {
            _gpuFrameTimer.EndFrame();
        }

        long submissionCompleted = Stopwatch.GetTimestamp();
        _swapChain.Present(1, PresentFlags.None).CheckError();
        return new RenderTiming(gpuTime, submissionCompleted, submitTime);
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

    /// <summary>What this process currently has resident in video memory, and its allowance.</summary>
    internal VideoMemoryUsage? QueryVideoMemory()
    {
        if (_videoMemoryAdapter is null)
        {
            return null;
        }

        QueryVideoMemoryInfo info = _videoMemoryAdapter.QueryVideoMemoryInfo(
            0,
            MemorySegmentGroup.Local);
        return new VideoMemoryUsage((long)info.CurrentUsage, (long)info.Budget);
    }

    public void Dispose()
    {
        ReleaseDeviceResources();
        _directWriteFactory.Dispose();
        _d2dFactory.Dispose();
    }

    private void CreateDeviceResources(ID3D11Device device)
    {
        _d3dDevice = device;
        _dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
        _d3dContext = _d3dDevice.ImmediateContext;
        _gpuFrameTimer = new GpuFrameTimer(_d3dDevice, _d3dContext);
        using IDXGIAdapter adapter = _dxgiDevice.GetAdapter();
        _dxgiFactory = adapter.GetParent<IDXGIFactory2>();
        // Kept for its video memory reporting, which the rest of the adapter is not needed for.
        _videoMemoryAdapter = adapter.QueryInterfaceOrNull<IDXGIAdapter3>();
        Log.Info("Native", $"Graphics device: {adapter.Description.Description}");

        _d2dDevice = _d2dFactory.CreateDevice(_dxgiDevice);
        DeviceContext = _d2dDevice.CreateDeviceContext();
        DeviceContext.SetDpi(_dpi, _dpi);
        StartupTrace.Mark("d2d");

        SwapChainDescription1 description = new(
            (uint)Math.Max(_width, 1),
            (uint)Math.Max(_height, 1),
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
            _window,
            description);
        _swapChain = swapChain.QueryInterface<IDXGISwapChain2>();
        _swapChain.MaximumFrameLatency = 1;
        _frameLatencyWaitHandle = new SafeWaitHandle(
            _swapChain.FrameLatencyWaitableObject,
            ownsHandle: true);

        CreateTargetBitmap();
        StartupTrace.Mark("swap-chain");
    }

    private void ReleaseDeviceResources()
    {
        ReleaseTargetBitmap();

        // A swap chain only leaves its window once nothing references its buffers any more,
        // so the pipeline has to be cleared and flushed before the chain is dropped.
        _d3dContext.ClearState();
        _d3dContext.Flush();

        DeviceContext.Dispose();
        _d2dDevice.Dispose();
        _frameLatencyWaitHandle.Dispose();
        _swapChain.Dispose();
        _videoMemoryAdapter?.Dispose();
        _videoMemoryAdapter = null;
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
