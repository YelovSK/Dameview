using System.Collections.Concurrent;
using Dameview.Imaging;
using Dameview.Imaging.Animation;
using Dameview.Imaging.Decoding;
using Dameview.Imaging.Loading;
using Dameview.UI.Presentation;
using Dameview.Win32;

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
            TestPolicy);
        using ImageLoadClient producer = service.CreateClient();
        using var cache = new RenderBitmapCache(1024, _ => { });
        using var loader = CreateLoader(producer, cache);
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
            TestPolicy);
        using ImageLoadClient producer = service.CreateClient();
        using var cache = new RenderBitmapCache(4, _ => { });
        using CachedBitmapLease current = cache.AddAndAcquire("current", null!, 1, 1);
        using var loader = CreateLoader(producer, cache);

        loader.Preload(["next"]);

        Assert.AreEqual(0, Volatile.Read(ref decodeCount));
    }

    [TestMethod]
    public void PreviewUsesCachedThumbnailWithoutDecodingAgain()
    {
        using var release = new ManualResetEventSlim();
        using var posted = new BlockingCollection<Action>();
        var source = new FakeThumbnailSource();
        using var thumbCache = new RenderBitmapCache(2L * 512 * 512 * 4, _ => { });
        thumbCache.AddInactive("image", null!, 512, 512);
        var thumbnails = new ThumbnailImageLoader(
            source,
            thumbCache,
            new WindowSynchronizationContext(posted.Add),
            _ => null!);
        using var service = new ImageLoadService(
            new WindowSynchronizationContext(posted.Add),
            new FakeBackend(() =>
            {
                release.Wait();
                return CreateUpload(100, 100);
            }),
            TestPolicy);
        using ImageLoadClient producer = service.CreateClient();
        using var cache = new RenderBitmapCache(1024, _ => { });
        using var loader = new PresentationImageLoader(
            producer,
            cache,
            _ => null!,
            _ => null!,
            thumbnails,
            new WindowSynchronizationContext(posted.Add));
        var results = new List<ImageLoaded>();

        try
        {
            loader.Load("image", result => results.Add((ImageLoaded)result));
            PumpUntil(posted, () => results.Count > 0);

            Assert.HasCount(1, results);
            Assert.IsTrue(results[0].IsPreview);
            Assert.AreEqual(512, results[0].Representation.Width);
            Assert.AreEqual(512, results[0].Representation.Height);
            Assert.IsInstanceOfType<CachedBitmapRepresentation>(results[0].Representation);
            Assert.AreEqual(0, source.Requests);

            release.Set();
            PumpUntil(posted, () => results.Count > 1);

            Assert.HasCount(2, results);
            Assert.IsFalse(results[1].IsPreview);
            Assert.AreEqual(100, results[1].Representation.Width);
            Assert.AreEqual(100, results[1].Representation.Height);
        }
        finally
        {
            release.Set();
            foreach (ImageLoaded result in results)
            {
                result.Dispose();
            }
        }
    }

    [TestMethod]
    public void PreviewArrivesBeforeFullDecode()
    {
        using var releaseDecode = new ManualResetEventSlim();
        using var posted = new BlockingCollection<Action>();
        var source = new FakeThumbnailSource();
        using var thumbCache = new RenderBitmapCache(1024, _ => { });
        var thumbnails = new ThumbnailImageLoader(
            source,
            thumbCache,
            new WindowSynchronizationContext(posted.Add),
            _ => null!);
        using var service = new ImageLoadService(
            new WindowSynchronizationContext(posted.Add),
            new FakeBackend(() =>
            {
                releaseDecode.Wait();
                return CreateUpload(9, 9);
            }),
            TestPolicy);
        using ImageLoadClient producer = service.CreateClient();
        using var cache = new RenderBitmapCache(1024, _ => { });
        using var loader = new PresentationImageLoader(
            producer,
            cache,
            _ => null!,
            _ => null!,
            thumbnails,
            new WindowSynchronizationContext(posted.Add));
        var results = new List<ImageLoaded>();

        try
        {
            loader.Load("image", result => results.Add((ImageLoaded)result));
            Assert.IsNotNull(source.Pending);
            source.Pending(CreateUpload(512, 512));
            PumpUntil(posted, () => results.Count > 0);

            Assert.IsTrue(results[0].IsPreview);
            Assert.AreEqual(512, results[0].Representation.Width);
            Assert.AreEqual(512, results[0].Representation.Height);
            Assert.IsInstanceOfType<CachedBitmapRepresentation>(results[0].Representation);
        }
        finally
        {
            releaseDecode.Set();
            foreach (ImageLoaded result in results)
            {
                result.Dispose();
            }
        }
    }

    [TestMethod]
    public void PreviewLeaseIsDisposedWhenUiPostThrows()
    {
        using var service = new ImageLoadService(
            new WindowSynchronizationContext(action => action()),
            new FakeBackend(() => CreateUpload()),
            TestPolicy);
        using ImageLoadClient producer = service.CreateClient();
        using var thumbnailCache = new RenderBitmapCache(1024, _ => { });
        using CachedBitmapLease previewLease = thumbnailCache.AddAndAcquire(
            "image",
            null!,
            1,
            1);
        using var renderCache = new RenderBitmapCache(1024, _ => { });
        using var loader = new PresentationImageLoader(
            producer,
            renderCache,
            _ => null!,
            _ => null!,
            new ImmediateThumbnailImageLoader(previewLease),
            new ThrowingSynchronizationContext());

        loader.Load("image", result => (result as ImageLoaded)?.Dispose());

        Assert.IsTrue(SpinWait.SpinUntil(
            () => previewLease.Bitmap.PinCount == 0,
            TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public void PreviewUsesThumbnailDisplaySize()
    {
        using var posted = new BlockingCollection<Action>();
        var source = new FakeThumbnailSource();
        using var thumbCache = new RenderBitmapCache(1024, _ => { });
        var thumbnails = new ThumbnailImageLoader(
            source,
            thumbCache,
            new WindowSynchronizationContext(posted.Add),
            _ => null!);
        using var blocked = new ManualResetEventSlim();
        using var service = new ImageLoadService(
            new WindowSynchronizationContext(posted.Add),
            new FakeBackend(() =>
            {
                blocked.Wait();
                return CreateUpload();
            }),
            TestPolicy);
        using ImageLoadClient producer = service.CreateClient();
        using var cache = new RenderBitmapCache(1024, _ => { });
        using var loader = new PresentationImageLoader(
            producer,
            cache,
            _ => null!,
            _ => null!,
            thumbnails,
            new WindowSynchronizationContext(posted.Add));
        ImageLoaded? preview = null;

        try
        {
            loader.Load("image", result =>
            {
                if (result is ImageLoaded loaded && loaded.IsPreview)
                {
                    preview = loaded;
                }
            });
            Assert.IsNotNull(source.Pending);
            source.Pending(CreateUpload(512, 256));
            PumpUntil(posted, () => preview is not null);

            Assert.AreEqual(512, preview!.Representation.Width);
            Assert.AreEqual(256, preview.Representation.Height);
        }
        finally
        {
            blocked.Set();
            preview?.Dispose();
        }
    }

    [TestMethod]
    public void LatePreviewCannotReplaceFullImage()
    {
        using var releasePreview = new ManualResetEventSlim();
        using var previewStarted = new ManualResetEventSlim();
        using var posted = new BlockingCollection<Action>();
        var source = new BlockingThumbnailSource(previewStarted, releasePreview);
        using var thumbCache = new RenderBitmapCache(1024, _ => { });
        var thumbnails = new ThumbnailImageLoader(
            source,
            thumbCache,
            new WindowSynchronizationContext(posted.Add),
            _ => null!);
        using var service = new ImageLoadService(
            new WindowSynchronizationContext(posted.Add),
            new FakeBackend(() => CreateUpload()),
            TestPolicy);
        using ImageLoadClient producer = service.CreateClient();
        using var cache = new RenderBitmapCache(1024, _ => { });
        using var loader = new PresentationImageLoader(
            producer,
            cache,
            _ => null!,
            _ => null!,
            thumbnails,
            new WindowSynchronizationContext(posted.Add));
        var results = new List<ImageLoaded>();

        try
        {
            loader.Load("image", result => results.Add((ImageLoaded)result));
            PumpUntil(posted, () => results.Count > 0);
            Assert.IsFalse(results[0].IsPreview);

            Assert.IsTrue(previewStarted.Wait(TimeSpan.FromSeconds(5)));
            releasePreview.Set();
            source.Complete();
            PumpPosted(posted);

            Assert.HasCount(1, results);
            Assert.IsFalse(results[0].IsPreview);
        }
        finally
        {
            releasePreview.Set();
            foreach (ImageLoaded result in results)
            {
                result.Dispose();
            }
        }
    }

    [TestMethod]
    public void ThumbnailFailureDoesNotPreventFullDecode()
    {
        using var posted = new BlockingCollection<Action>();
        var source = new FakeThumbnailSource();
        using var thumbCache = new RenderBitmapCache(1024, _ => { });
        var thumbnails = new ThumbnailImageLoader(
            source,
            thumbCache,
            new WindowSynchronizationContext(posted.Add),
            _ => null!);
        using var service = new ImageLoadService(
            new WindowSynchronizationContext(posted.Add),
            new FakeBackend(() => CreateUpload()),
            TestPolicy);
        using ImageLoadClient producer = service.CreateClient();
        using var cache = new RenderBitmapCache(1024, _ => { });
        using var loader = new PresentationImageLoader(
            producer,
            cache,
            _ => null!,
            _ => null!,
            thumbnails,
            new WindowSynchronizationContext(posted.Add));
        ImageLoadResult? result = null;

        loader.Load("image", loaded => result = loaded);
        PumpUntil(posted, () => result is not null);

        Assert.IsInstanceOfType<ImageLoaded>(result);
        Assert.IsFalse(((ImageLoaded)result).IsPreview);
        ((ImageLoaded)result).Dispose();
    }

    private static PresentationImageLoader CreateLoader(
        ImageLoadClient producer,
        RenderBitmapCache cache)
    {
        return new PresentationImageLoader(
            producer,
            cache,
            _ => null!,
            _ => null!,
            NoThumbnailImageLoader.Instance,
            new WindowSynchronizationContext(action => action()));
    }

    private static void PumpUntil(BlockingCollection<Action> posted, Func<bool> done)
    {
        while (!done())
        {
            Assert.IsTrue(posted.TryTake(out Action? action, TimeSpan.FromSeconds(5)));
            action();
        }
    }

    private static void PumpPosted(BlockingCollection<Action> posted)
    {
        while (posted.TryTake(out Action? action, TimeSpan.FromMilliseconds(50)))
        {
            action();
        }
    }

    private static DecodedImageUpload CreateUpload(int width = 1, int height = 1)
    {
        int stride = checked(width * 4);
        return DecodedImageUpload.Allocate(width, height, stride);
    }

    private static DecodedImage CreateImage(int width = 1, int height = 1)
    {
        int stride = checked(width * 4);
        return new DecodedImage(width, height, stride, new byte[checked(stride * height)]);
    }

    private sealed class FakeBackend(Func<DecodedImageUpload> decode) : IImageLoadingBackend
    {
        public IImageDecoder CreateDecoder() => new FakeDecoder(decode);
        public IImageTileSource OpenTiledImage(string path) => throw new NotSupportedException();
        public DecodedImageUpload? LoadThumbnail(string path) => null;
        public bool SupportsAnimation(string path) => false;
        public IAnimationSession OpenAnimation(string path) => throw new NotSupportedException();
    }

    private sealed class FakeDecoder(Func<DecodedImageUpload> decode) : IImageDecoder
    {
        public ImageInfo GetInfo(string path) => new(1, 1, 1);
        public DecodedImageUpload DecodeUpload(
            string path,
            CancellationToken cancellationToken = default) => decode();
        public void Dispose()
        {
        }
    }

    private sealed class NoThumbnailImageLoader : IThumbnailImageLoader
    {
        internal static readonly NoThumbnailImageLoader Instance = new();

        public IDisposable Request(
            string path,
            ThumbnailPriority priority,
            Action<CachedBitmapLease> completed) => NoopSubscription.Instance;
    }

    private sealed class ImmediateThumbnailImageLoader(CachedBitmapLease lease) : IThumbnailImageLoader
    {
        public IDisposable Request(
            string path,
            ThumbnailPriority priority,
            Action<CachedBitmapLease> completed)
        {
            completed(lease);
            return NoopSubscription.Instance;
        }
    }

    private sealed class ThrowingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) =>
            throw new InvalidOperationException("Test UI post failure.");
    }

    private sealed class FakeThumbnailSource : IThumbnailLoader
    {
        internal int Requests;
        internal Action<DecodedImageUpload>? Pending;

        public IDisposable Request(
            string path,
            ThumbnailPriority priority,
            Action<DecodedImageUpload> completed)
        {
            Requests++;
            Pending = completed;
            return NoopSubscription.Instance;
        }
    }

    private sealed class BlockingThumbnailSource(
        ManualResetEventSlim started,
        ManualResetEventSlim release) : IThumbnailLoader
    {
        private Action<DecodedImageUpload>? _completed;

        public IDisposable Request(
            string path,
            ThumbnailPriority priority,
            Action<DecodedImageUpload> completed)
        {
            _completed = completed;
            started.Set();
            return NoopSubscription.Instance;
        }

        internal void Complete()
        {
            release.Wait();
            _completed!(CreateUpload());
        }
    }

    private sealed class NoopSubscription : IDisposable
    {
        internal static readonly NoopSubscription Instance = new();

        public void Dispose()
        {
        }
    }
}
