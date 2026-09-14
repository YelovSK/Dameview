using Dameview.Imaging;
using Dameview.Platform;
using Dameview.UI;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class PresentationImageLoaderTests
{
    private static readonly ImageRepresentationPolicy TestPolicy = new(16_384);

    [TestMethod]
    public void SecondLoadUsesRenderCacheWithoutDecodingAgain()
    {
        using var firstCompleted = new ManualResetEventSlim();
        int decodeCount = 0;
        using var service = new ImageLoadService(
            new WindowSynchronizationContext(action => action()),
            new FakeBackend(() =>
            {
                Interlocked.Increment(ref decodeCount);
                return CreateUpload();
            }),
            TestPolicy,
            NoThumbnailLoader.Instance,
            NoImageInfoLoader.Instance);
        using ImageLoadClient producer = service.CreateClient();
        using var cache = new RenderBitmapCache(1024, _ => { });
        using var loader = new PresentationImageLoader(
            producer,
            cache,
            _ => null!,
            _ => null!);
        ImageLoaded? first = null;

        loader.Load("image", result =>
        {
            first = (ImageLoaded)result;
            firstCompleted.Set();
        });
        Assert.IsTrue(firstCompleted.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsInstanceOfType<CachedBitmapRepresentation>(first!.Representation);
        first.Dispose();

        ImageLoaded? second = null;
        loader.Load("image", result => second = (ImageLoaded)result);

        Assert.IsNotNull(second);
        Assert.IsInstanceOfType<CachedBitmapRepresentation>(second.Representation);
        Assert.AreEqual(1, Volatile.Read(ref decodeCount));
        second.Dispose();
    }

    [TestMethod]
    public void FullRenderCacheSuppressesSpeculativeDecode()
    {
        int decodeCount = 0;
        using var service = new ImageLoadService(
            new WindowSynchronizationContext(action => action()),
            new FakeBackend(() =>
            {
                Interlocked.Increment(ref decodeCount);
                return CreateUpload();
            }),
            TestPolicy,
            NoThumbnailLoader.Instance,
            NoImageInfoLoader.Instance);
        using ImageLoadClient producer = service.CreateClient();
        using var cache = new RenderBitmapCache(4, _ => { });
        using CachedBitmapLease current = cache.AddAndAcquire("current", null!, 1, 1);
        using var loader = new PresentationImageLoader(
            producer,
            cache,
            _ => null!,
            _ => null!);

        loader.Preload(["next"]);

        Assert.AreEqual(0, Volatile.Read(ref decodeCount));
    }

    private static DecodedImageUpload CreateUpload()
    {
        return DecodedImageUpload.Allocate(1, 1, 4);
    }

    private sealed class FakeBackend(Func<DecodedImageUpload> decode) : IImageLoadingBackend
    {
        public IImageDecoder CreateDecoder() => new FakeDecoder(decode);
        public IImageTileSource OpenTiledImage(string path) => throw new NotSupportedException();
        public DecodedImage? LoadThumbnail(string path) => null;
        public bool SupportsAnimation(string path) => false;
        public IAnimationSession OpenAnimation(string path) => throw new NotSupportedException();
    }

    private sealed class FakeDecoder(Func<DecodedImageUpload> decode) : IImageDecoder
    {
        public ImageInfo GetInfo(string path) => new(1, 1);
        public DecodedImageUpload DecodeUpload(
            string path,
            CancellationToken cancellationToken = default) => decode();
        public void Dispose()
        {
        }
    }

    private sealed class NoThumbnailLoader : IThumbnailLoader
    {
        internal static readonly NoThumbnailLoader Instance = new();

        public IDisposable Request(
            string path,
            ThumbnailPriority priority,
            Action<DecodedImage> completed) => NoopSubscription.Instance;

        private sealed class NoopSubscription : IDisposable
        {
            internal static readonly NoopSubscription Instance = new();
            public void Dispose()
            {
            }
        }
    }

    private sealed class NoImageInfoLoader : IImageInfoLoader
    {
        internal static readonly NoImageInfoLoader Instance = new();

        public Task<ImageInfo> LoadAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(new ImageInfo(1, 1));
    }
}
