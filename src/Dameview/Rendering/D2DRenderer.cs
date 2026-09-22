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

/// <summary>Owns the graphics device and the frame lifecycle. The factories outlive device switches.</summary>
internal sealed class D2DRenderer : IDisposable
{
    private const uint BufferCount = 2;

    private readonly nint _window;
    private readonly ID2D1Factory1 _d2dFactory;
    private readonly IDWriteFactory1 _directWriteFactory;

    private DeviceResources _device;
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

        _device = CreateDeviceResources(device);
        CreateTargetBitmap();
    }

    internal nint FrameLatencyWaitHandle => _device.FrameLatencyWaitHandle.DangerousGetHandle();
    internal ID2D1DeviceContext DeviceContext => _device.DeviceContext;
    internal IDWriteFactory DirectWriteFactory => _directWriteFactory;

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
    /// <see cref="DeviceContext"/> before the next frame, whether or not this succeeds.
    /// </summary>
    /// <returns>
    /// False when <paramref name="device"/> could not be set up and the renderer fell back to
    /// a new software device instead.
    /// </returns>
    internal bool AdoptDevice(ID3D11Device device)
    {
        // A window holds only one swap chain, so the current one has to go first, and there is
        // nothing to return to afterwards. The software rasterizer is always there instead.
        ReleaseTargetBitmap();
        _device.Dispose();
        bool adopted = true;
        try
        {
            _device = CreateDeviceResources(device);
        }
        catch (Exception exception)
        {
            Log.Warning("Native", $"Could not switch the graphics device: {exception.Message}");
            _device = CreateDeviceResources(CreateDevice(DriverType.Warp));
            adopted = false;
        }

        CreateTargetBitmap();
        return adopted;
    }

    internal RenderTiming Render(Action<SizeF> draw, Color4 background, bool measureGpu = false)
    {
        if (_width <= 0 || _height <= 0)
        {
            return new RenderTiming(null, Stopwatch.GetTimestamp());
        }

        // Clear before timing: the first backbuffer write can wait for presentation.
        // Match the premultiplied Direct2D target without splitting its draw batch.
        _device.D3DContext.ClearRenderTargetView(_targetView!, new Color4(
            background.R * background.A,
            background.G * background.A,
            background.B * background.A,
            background.A));
        TimeSpan? gpuTime = measureGpu ? _device.GpuFrameTimer.BeginFrame() : null;
        DeviceContext.BeginDraw();
        draw(new SizeF(_width, _height));

        long recordingCompleted = Stopwatch.GetTimestamp();
        DeviceContext.EndDraw().CheckError();
        TimeSpan submitTime = Stopwatch.GetElapsedTime(recordingCompleted);
        if (measureGpu)
        {
            _device.GpuFrameTimer.EndFrame();
        }

        long submissionCompleted = Stopwatch.GetTimestamp();
        _device.SwapChain.Present(1, PresentFlags.None).CheckError();
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
        _device.SwapChain.ResizeBuffers(
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
        if (_device.VideoMemoryAdapter is not { } adapter)
        {
            return null;
        }

        QueryVideoMemoryInfo info = adapter.QueryVideoMemoryInfo(
            0,
            MemorySegmentGroup.Local);
        return new VideoMemoryUsage((long)info.CurrentUsage, (long)info.Budget);
    }

    public void Dispose()
    {
        ReleaseTargetBitmap();
        _device.Dispose();
        _directWriteFactory.Dispose();
        _d2dFactory.Dispose();
    }

    private DeviceResources CreateDeviceResources(ID3D11Device device) =>
        new(device, _d2dFactory, _window, _width, _height, _dpi);

    private void CreateTargetBitmap()
    {
        using IDXGISurface surface = _device.SwapChain.GetBuffer<IDXGISurface>(0);
        using ID3D11Texture2D texture = surface.QueryInterface<ID3D11Texture2D>();
        _targetView = _device.D3DDevice.CreateRenderTargetView(texture);
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

    /// <summary>
    /// Everything built from one Direct3D device, which it takes ownership of. It is released
    /// as a unit, also when its creation fails partway, so a failed device switch can recover.
    /// </summary>
    private sealed class DeviceResources : IDisposable
    {
        internal DeviceResources(
            ID3D11Device device,
            ID2D1Factory1 d2dFactory,
            nint window,
            int width,
            int height,
            float dpi)
        {
            D3DDevice = device;
            try
            {
                DxgiDevice = D3DDevice.QueryInterface<IDXGIDevice>();
                D3DContext = D3DDevice.ImmediateContext;
                GpuFrameTimer = new GpuFrameTimer(D3DDevice, D3DContext);
                using IDXGIAdapter adapter = DxgiDevice.GetAdapter();
                DxgiFactory = adapter.GetParent<IDXGIFactory2>();
                VideoMemoryAdapter = adapter.QueryInterfaceOrNull<IDXGIAdapter3>();
                Log.Info("Native", $"Graphics device: {adapter.Description.Description}");

                D2DDevice = d2dFactory.CreateDevice(DxgiDevice);
                DeviceContext = D2DDevice.CreateDeviceContext();
                DeviceContext.SetDpi(dpi, dpi);
                StartupTrace.Mark("d2d");

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

                using IDXGISwapChain1 swapChain = DxgiFactory.CreateSwapChainForHwnd(
                    D3DDevice,
                    window,
                    description);
                SwapChain = swapChain.QueryInterface<IDXGISwapChain2>();
                SwapChain.MaximumFrameLatency = 1;
                FrameLatencyWaitHandle = new SafeWaitHandle(
                    SwapChain.FrameLatencyWaitableObject,
                    ownsHandle: true);
                StartupTrace.Mark("swap-chain");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal ID3D11Device D3DDevice { get; }
        internal ID3D11DeviceContext D3DContext { get; }
        internal GpuFrameTimer GpuFrameTimer { get; }
        internal IDXGIDevice DxgiDevice { get; }
        // Kept for its video memory reporting, which the rest of the adapter is not needed for.
        internal IDXGIAdapter3? VideoMemoryAdapter { get; }
        internal IDXGIFactory2 DxgiFactory { get; }
        internal ID2D1Device D2DDevice { get; }
        internal ID2D1DeviceContext DeviceContext { get; }
        internal IDXGISwapChain2 SwapChain { get; }
        internal SafeWaitHandle FrameLatencyWaitHandle { get; }

        // Null-tolerant, because a failed construction leaves only some of these assigned.
        public void Dispose()
        {
            // A swap chain only leaves its window once nothing references its buffers any more,
            // so the pipeline has to be cleared and flushed before the chain is dropped.
            D3DContext?.ClearState();
            D3DContext?.Flush();

            DeviceContext?.Dispose();
            D2DDevice?.Dispose();
            FrameLatencyWaitHandle?.Dispose();
            SwapChain?.Dispose();
            VideoMemoryAdapter?.Dispose();
            DxgiFactory?.Dispose();
            DxgiDevice?.Dispose();
            GpuFrameTimer?.Dispose();
            D3DContext?.Dispose();
            D3DDevice.Dispose();
        }
    }
}
