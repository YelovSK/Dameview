using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Dameview.Imaging;
using Dameview.Imaging.Animation;
using Dameview.Imaging.Decoding;
using Dameview.Imaging.Loading;
using Dameview.Rendering;
using Dameview.Win32;
using Vortice.Direct2D1;

namespace Dameview.Tests.Imaging;

/// <summary>Runs a pipeline with the test thread as its window thread.</summary>
internal sealed class PipelineHarness : IDisposable
{
    internal static readonly ImageRepresentationPolicy TestPolicy = new(16_384);

    private readonly BlockingCollection<Action> _posted = [];

    internal PipelineHarness(
        IImageSource full,
        IImageSource? thumbnails = null,
        long fullCacheBytes = 1L << 20,
        FakeUploader? uploader = null)
    {
        Uploader = uploader ?? new FakeUploader();
        Pipeline = new ImagePipeline(
            new WindowSynchronizationContext(_posted.Add),
            Uploader,
            (full, new RenderBitmapCache(fullCacheBytes, _ => { })),
            (thumbnails ?? new FakeSource(_ => throw new InvalidDataException("No thumbnail.")),
                new RenderBitmapCache(1L << 20, _ => { })));
    }

    internal ImagePipeline Pipeline { get; }
    internal FakeUploader Uploader { get; }

    internal void PumpUntil(Func<bool> done)
    {
        while (!done())
        {
            Assert.IsTrue(_posted.TryTake(out Action? action, TimeSpan.FromSeconds(5)), "Nothing was posted.");
            action();
        }
    }

    /// <summary>Runs whatever arrives within a short while.</summary>
    internal void PumpPending()
    {
        while (_posted.TryTake(out Action? action, TimeSpan.FromMilliseconds(100)))
        {
            action();
        }
    }

    public void Dispose()
    {
        Pipeline.Dispose();
        _posted.Dispose();
    }

    internal static UploadImageRepresentation CreateImage(int width = 1, int height = 1) =>
        new(DecodedImageUpload.Allocate(width, height, width * 4));
}

internal sealed class FakeSource(
    Func<string, ImageRepresentation?> load,
    int workerCount = 1) : IImageSource
{
    private readonly ConcurrentQueue<string> _loaded = new();

    internal FakeSource(Func<string, ImageLoadContext, ImageRepresentation?> load, int workerCount = 1)
        : this(path => throw new InvalidOperationException(), workerCount)
    {
        LoadWithContext = load;
    }

    internal Func<string, ImageLoadContext, ImageRepresentation?>? LoadWithContext { get; }
    internal string[] Loaded => [.. _loaded];

    public string WorkerName => "Test source";
    public int WorkerCount => workerCount;

    public object? CreateWorkerState() => null;

    public ImageRepresentation? Load(object? workerState, string path, ImageLoadContext context)
    {
        _loaded.Enqueue(path);
        return LoadWithContext is { } loadWithContext ? loadWithContext(path, context) : load(path);
    }
}

internal sealed class FakeUploader : IBitmapUploader
{
    private int _staleUploads;
    private int _uploads;

    internal int Uploads => Volatile.Read(ref _uploads);

    /// <summary>Makes the next uploads come back as if the device had changed right after.</summary>
    internal void SwitchDeviceAfterNextUploads(int count) => _staleUploads = count;

    public IUploadedBitmap Upload(ImageRepresentation image)
    {
        Interlocked.Increment(ref _uploads);
        return new UploadedBitmap(Interlocked.Decrement(ref _staleUploads) >= 0);
    }

    private sealed class UploadedBitmap(bool stale) : IUploadedBitmap
    {
        public bool TryCreateBitmap([NotNullWhen(true)] out ID2D1Bitmap1? bitmap)
        {
            // Wraps nothing, so the pipeline can dispose it like a real one.
            bitmap = new ID2D1Bitmap1(nint.Zero);
            return !stale;
        }

        public void Dispose()
        {
        }
    }
}

internal sealed class FakeLoadContext(Func<long?, bool>? shouldLoad = null) : ImageLoadContext
{
    internal List<long?> Asked { get; } = [];
    internal override CancellationToken CancellationToken => CancellationToken.None;

    internal override bool ShouldLoad(long? cachedBytes)
    {
        Asked.Add(cachedBytes);
        return shouldLoad?.Invoke(cachedBytes) ?? true;
    }
}

internal sealed class FakeImageDecoder(
    Func<string, DecodedImage> decode,
    Func<string, ImageInfo>? getInfo = null) : IImageDecoder
{
    public ImageInfo GetInfo(string path) => getInfo?.Invoke(path) ?? new ImageInfo(1, 1, 1);

    public DecodedImageUpload DecodeUpload(string path, CancellationToken cancellationToken = default)
    {
        DecodedImage image = decode(path);
        var upload = DecodedImageUpload.Allocate(image.Width, image.Height, image.Stride);
        image.Pixels.CopyTo(upload.Span);
        return upload;
    }

    public ClipboardBitmap DecodeClipboardBitmap(string path, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public void Dispose()
    {
    }

    internal static DecodedImage CreateImage(int width = 1, int height = 1)
    {
        int stride = checked(width * 4);
        return new DecodedImage(width, height, stride, new byte[checked(stride * height)]);
    }
}

internal sealed class FakeImageLoadingBackend(
    Func<IImageDecoder> createDecoder,
    IAnimatedImageDecoder? animatedDecoder = null,
    Func<string, IImageTileSource>? openTiledImage = null) : IImageLoadingBackend
{
    public IImageDecoder CreateDecoder() => createDecoder();

    public IImageTileSource OpenTiledImage(string path) =>
        openTiledImage?.Invoke(path)
            ?? throw new NotSupportedException("Tiled images are not configured for this test.");

    public DecodedImageUpload? LoadThumbnail(string path) => null;

    public bool SupportsAnimation(string path) => animatedDecoder?.CanDecode(path) == true;

    public IAnimationSession OpenAnimation(string path) =>
        animatedDecoder?.Open(path)
            ?? throw new NotSupportedException("Animation is not configured for this test.");
}

internal sealed class FakeAnimatedImageDecoder(Func<string, IAnimationSession> open) : IAnimatedImageDecoder
{
    public bool CanDecode(string path) => true;

    public IAnimationSession Open(string path) => open(path);
}

internal sealed class FakeAnimationSession : IAnimationSession
{
    public AnimationFrame FirstFrame { get; } =
        new(FakeImageDecoder.CreateImage(), TimeSpan.FromMilliseconds(100));

    public bool IsAnimated => true;
    public bool IsComplete => false;
    public Exception? Error => null;
    public bool IsDisposed { get; private set; }

    public bool TryGetReadyFrame(out AnimationFrame frame)
    {
        frame = null!;
        return false;
    }

    public void Dispose() => IsDisposed = true;
}

internal sealed class FakeTileSource : IImageTileSource
{
    public int Width => 20_000;
    public int Height => 10_000;
    public ImageOrientation Orientation => default;
    public int TileSize => 2048;
    public DecodedImage Overview { get; } = FakeImageDecoder.CreateImage();
    internal bool IsDisposed { get; private set; }

    public IImageTileDecoder CreateTileDecoder() => throw new NotSupportedException();

    public void Dispose() => IsDisposed = true;
}
