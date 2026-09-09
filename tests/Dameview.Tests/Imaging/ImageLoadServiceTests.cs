using System.Collections.Concurrent;
using Dameview.Imaging;
using Dameview.Platform;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class ImageLoadServiceTests
{
    private static readonly ImageRepresentationPolicy TestPolicy = new(16_384);

    [TestMethod]
    public void ClientsLoadIndependently()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var firstCompleted = new ManualResetEventSlim();
        using var secondCompleted = new ManualResetEventSlim();
        using var service = new ImageLoadService(
            action => action(),
            new FakeImageLoadingBackend(() => new FakeImageDecoder(path =>
            {
                if (path == "first")
                {
                    firstStarted.Set();
                    releaseFirst.Wait();
                }

                return CreateImage();
            })),
            TestPolicy,
            NoThumbnailLoader.Instance);
        using ImageLoadClient first = service.CreateClient();
        using ImageLoadClient second = service.CreateClient();

        try
        {
            first.Load("first", result =>
            {
                DisposeResult(result);
                firstCompleted.Set();
            });
            Assert.IsTrue(firstStarted.Wait(TimeSpan.FromSeconds(5)));

            second.Load("second", result =>
            {
                DisposeResult(result);
                secondCompleted.Set();
            });

            Assert.IsTrue(secondCompleted.Wait(TimeSpan.FromSeconds(5)));
            releaseFirst.Set();
            Assert.IsTrue(firstCompleted.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            releaseFirst.Set();
        }
    }

    [TestMethod]
    public void ReplacingOneClientsPreloadsDoesNotClearAnotherClientsQueue()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var completed = new CountdownEvent(3);
        var decodedPaths = new ConcurrentQueue<string>();
        using var service = new ImageLoadService(
            action => action(),
            new FakeImageLoadingBackend(() => new FakeImageDecoder(path =>
            {
                decodedPaths.Enqueue(path);
                if (path == "first")
                {
                    firstStarted.Set();
                    releaseFirst.Wait();
                }

                return CreateImage();
            })),
            TestPolicy,
            NoThumbnailLoader.Instance);
        using ImageLoadClient first = service.CreateClient();
        using ImageLoadClient second = service.CreateClient();

        try
        {
            first.Preload(["first", "still-pending"], Complete);
            Assert.IsTrue(firstStarted.Wait(TimeSpan.FromSeconds(5)));
            second.Preload(["other-client"], Complete);
            releaseFirst.Set();

            Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
            CollectionAssert.AreEquivalent(
                new[] { "first", "still-pending", "other-client" },
                decodedPaths.ToArray());
        }
        finally
        {
            releaseFirst.Set();
        }

        void Complete(ImageLoadResult result)
        {
            DisposeResult(result);
            completed.Signal();
        }
    }

    [TestMethod]
    public void ActiveDecodeFinishesButOnlyTheNewestPendingRequestIsDelivered()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        var decodedPaths = new ConcurrentQueue<string>();
        var deliveredPaths = new ConcurrentQueue<string>();
        using var decoder = new FakeImageDecoder(path =>
        {
            decodedPaths.Enqueue(path);
            if (path == "first")
            {
                firstStarted.Set();
                releaseFirst.Wait();
            }

            return CreateImage();
        });
        using var coordinator = new TestClient(
            action => action(),
            new FakeImageLoadingBackend(() => decoder),
            TestPolicy,
            NoThumbnailLoader.Instance);

        coordinator.Load("first", RecordResult);
        Assert.IsTrue(firstStarted.Wait(TimeSpan.FromSeconds(5)));

        coordinator.Load("second", RecordResult);
        coordinator.Load("third", RecordResult);
        releaseFirst.Set();

        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
        CollectionAssert.AreEqual(
            new[] { "first", "third" },
            decodedPaths.ToArray());
        CollectionAssert.AreEqual(new[] { "third" }, deliveredPaths.ToArray());

        void RecordResult(ImageLoadResult result)
        {
            deliveredPaths.Enqueue(result.Path);
            completed.Set();
        }
    }

    [TestMethod]
    public void SupersededForegroundDecodeIsCancelledAndNotDelivered()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var firstCancelled = new ManualResetEventSlim();
        using var secondCompleted = new ManualResetEventSlim();
        using var decoder = new FakeImageDecoder((path, token) =>
        {
            if (path == "first")
            {
                firstStarted.Set();
                token.WaitHandle.WaitOne();
                firstCancelled.Set();
                token.ThrowIfCancellationRequested();
            }

            return CreateImage();
        });
        using var coordinator = new TestClient(
            action => action(),
            new FakeImageLoadingBackend(() => decoder),
            TestPolicy,
            NoThumbnailLoader.Instance);

        coordinator.Load("first", _ => Assert.Fail("Cancelled result was delivered."));
        Assert.IsTrue(firstStarted.Wait(TimeSpan.FromSeconds(5)));
        coordinator.Load("second", result =>
        {
            DisposeResult(result);
            secondCompleted.Set();
        });

        Assert.IsTrue(firstCancelled.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(secondCompleted.Wait(TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public void DecodeFailuresAreDeliveredAsResults()
    {
        using var completed = new ManualResetEventSlim();
        ImageLoadResult? deliveredResult = null;
        using var decoder = new FakeImageDecoder(
            _ => throw new InvalidDataException("Broken image"));
        using var coordinator = new TestClient(
            action => action(),
            new FakeImageLoadingBackend(() => decoder),
            TestPolicy,
            NoThumbnailLoader.Instance);

        coordinator.Load("broken.jpg", result =>
        {
            deliveredResult = result;
            completed.Set();
        });

        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsInstanceOfType<ImageLoadFailed>(deliveredResult);
        Assert.AreEqual("broken.jpg", deliveredResult.Path);
    }

    [TestMethod]
    public void PostedResultIsIgnoredIfANewerRequestArrivesBeforeUiDelivery()
    {
        using var resultPosted = new SemaphoreSlim(0);
        var postedActions = new ConcurrentQueue<Action>();
        var deliveredPaths = new List<string>();
        using var decoder = new FakeImageDecoder(_ => CreateImage());
        using var coordinator = new TestClient(
            action =>
            {
                postedActions.Enqueue(action);
                resultPosted.Release();
            },
            new FakeImageLoadingBackend(() => decoder),
            TestPolicy,
            NoThumbnailLoader.Instance);

        coordinator.Load("first", result => deliveredPaths.Add(result.Path));
        Assert.IsTrue(resultPosted.Wait(TimeSpan.FromSeconds(5)));

        coordinator.Load("second", result => deliveredPaths.Add(result.Path));
        Assert.IsTrue(postedActions.TryDequeue(out Action? deliverFirst));
        deliverFirst();

        Assert.IsTrue(resultPosted.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(postedActions.TryDequeue(out Action? deliverSecond));
        deliverSecond();

        CollectionAssert.AreEqual(new[] { "second" }, deliveredPaths);
    }

    [TestMethod]
    public void SupersededAnimationIsDisposedBeforeUiDelivery()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var secondDelivered = new ManualResetEventSlim();
        var firstSession = new FakeAnimationSession();
        var secondSession = new FakeAnimationSession();
        using var decoder = new FakeAnimatedImageDecoder(path =>
        {
            if (path == "first.gif")
            {
                firstStarted.Set();
                releaseFirst.Wait();
                return firstSession;
            }

            return secondSession;
        });
        using var coordinator = new TestClient(
            action => action(),
            new FakeImageLoadingBackend(() => decoder, decoder),
            TestPolicy,
            NoThumbnailLoader.Instance);

        try
        {
            coordinator.Load("first.gif", _ => Assert.Fail("Superseded result was delivered."));
            Assert.IsTrue(firstStarted.Wait(TimeSpan.FromSeconds(5)));

            coordinator.Load("second.gif", result =>
            {
                ((ImageLoaded)result).Dispose();
                secondDelivered.Set();
            });
            releaseFirst.Set();

            Assert.IsTrue(secondDelivered.Wait(TimeSpan.FromSeconds(5)));
            Assert.IsTrue(firstSession.IsDisposed);
        }
        finally
        {
            releaseFirst.Set();
        }
    }

    [TestMethod]
    public void AnimatedImageSkipsThumbnailPreview()
    {
        using var completed = new ManualResetEventSlim();
        int thumbnailLoads = 0;
        using var decoder = new FakeAnimatedImageDecoder(_ => new FakeAnimationSession());
        using var coordinator = new TestClient(
            action => action(),
            new FakeImageLoadingBackend(
                () => decoder,
                decoder,
                _ =>
                {
                    Interlocked.Increment(ref thumbnailLoads);
                    return CreateImage();
                }),
            TestPolicy,
            NoThumbnailLoader.Instance);

        coordinator.Load("animated.gif", result =>
        {
            ((ImageLoaded)result).Dispose();
            completed.Set();
        });

        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(0, Volatile.Read(ref thumbnailLoads));
    }

    [TestMethod]
    public void ForegroundUsesTiledRepresentationWithoutFullDecode()
    {
        using var completed = new ManualResetEventSlim();
        int decodeCount = 0;
        var tiles = new FakeTileSource();
        using var coordinator = new TestClient(
            action => action(),
            new FakeImageLoadingBackend(
                () => new FakeImageDecoder(
                    _ =>
                    {
                        Interlocked.Increment(ref decodeCount);
                        return CreateImage();
                    },
                    _ => new ImageInfo(20_000, 10_000)),
                openTiledImage: _ => tiles),
            TestPolicy,
            NoThumbnailLoader.Instance);
        ImageLoaded? loaded = null;

        coordinator.Load("large.png", result =>
        {
            loaded = (ImageLoaded)result;
            completed.Set();
        });

        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsInstanceOfType<TiledImageRepresentation>(loaded!.Representation);
        Assert.AreEqual(0, Volatile.Read(ref decodeCount));
        loaded.Dispose();
        Assert.IsTrue(tiles.IsDisposed);
    }

    [TestMethod]
    public void PreloadSkipsImagesThatRequireTiling()
    {
        using var sentinelDecoded = new ManualResetEventSlim();
        var decodedPaths = new ConcurrentQueue<string>();
        var openedTilePaths = new ConcurrentQueue<string>();
        using var coordinator = new TestClient(
            action => action(),
            new FakeImageLoadingBackend(
                () => new FakeImageDecoder(
                    path =>
                    {
                        decodedPaths.Enqueue(path);
                        if (path == "sentinel")
                        {
                            sentinelDecoded.Set();
                        }

                        return CreateImage();
                    },
                    path => path == "large"
                        ? new ImageInfo(20_000, 10_000)
                        : new ImageInfo(1, 1)),
                openTiledImage: path =>
                {
                    openedTilePaths.Enqueue(path);
                    return new FakeTileSource();
                }),
            TestPolicy,
            NoThumbnailLoader.Instance);

        coordinator.Preload(["large", "sentinel"], DisposeResult);

        Assert.IsTrue(sentinelDecoded.Wait(TimeSpan.FromSeconds(5)));
        CollectionAssert.AreEqual(new[] { "sentinel" }, decodedPaths.ToArray());
        Assert.IsEmpty(openedTilePaths);
    }

    [TestMethod]
    public void CompletedPreloadDeliversDisposableUpload()
    {
        using var completed = new ManualResetEventSlim();
        using var coordinator = new TestClient(
            action => action(),
            new FakeImageLoadingBackend(() => new FakeImageDecoder(_ => CreateImage())),
            TestPolicy,
            NoThumbnailLoader.Instance);
        ImageLoaded? loaded = null;

        coordinator.Preload(["next"], result =>
        {
            loaded = (ImageLoaded)result;
            completed.Set();
        });

        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsInstanceOfType<UploadImageRepresentation>(loaded!.Representation);
        loaded.Dispose();
    }

    [TestMethod]
    public void ForegroundLoadJoinsAnActivePreloadForTheSameImage()
    {
        using var decodeStarted = new ManualResetEventSlim();
        using var releaseDecode = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        int decodeCount = 0;

        DecodedImage Decode(string _)
        {
            Interlocked.Increment(ref decodeCount);
            decodeStarted.Set();
            releaseDecode.Wait();
            return CreateImage();
        }

        using var coordinator = new TestClient(
            action => action(),
            new FakeImageLoadingBackend(() => new FakeImageDecoder(Decode)),
            TestPolicy,
            NoThumbnailLoader.Instance);

        try
        {
            coordinator.Preload(["same"], DisposeResult);
            Assert.IsTrue(decodeStarted.Wait(TimeSpan.FromSeconds(5)));

            coordinator.Load("same", _ => completed.Set());
            releaseDecode.Set();

            Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
            Assert.AreEqual(1, Volatile.Read(ref decodeCount));
        }
        finally
        {
            releaseDecode.Set();
        }
    }

    [TestMethod]
    public void PreviewArrivesWhileDecodeIsBlockedAndFullImageReplacesIt()
    {
        using var release = new ManualResetEventSlim();
        using var posted = new BlockingCollection<Action>();
        using var thumbnails = new ThumbnailCoordinator(posted.Add, _ => CreateImage(512, 512));
        using var coordinator = new TestClient(posted.Add,
            new FakeImageLoadingBackend(
                () => new FakeImageDecoder(
                    _ => { release.Wait(); return CreateImage(100, 100); },
                    _ => new ImageInfo(100, 100))),
            TestPolicy,
            thumbnails);
        var results = new List<ImageLoaded>();
        try
        {
            coordinator.Load("image", result => results.Add((ImageLoaded)result));
            Assert.IsTrue(posted.TryTake(out Action? preview, TimeSpan.FromSeconds(5)));
            preview();
            Assert.IsTrue(results[0].IsPreview);
            Assert.AreEqual(100, results[0].Representation.Width);
            Assert.AreEqual(100, results[0].Representation.Height);
            var previewImage = (DecodedImageRepresentation)results[0].Representation;
            Assert.AreEqual(512, previewImage.Image.Width);
            Assert.AreEqual(512, previewImage.Image.Height);
            release.Set();
            Assert.IsTrue(posted.TryTake(out Action? full, TimeSpan.FromSeconds(5)));
            full();
            Assert.HasCount(2, results);
            Assert.IsFalse(results[1].IsPreview);
            Assert.AreEqual(results[0].Representation.Width, results[1].Representation.Width);
            Assert.AreEqual(results[0].Representation.Height, results[1].Representation.Height);
        }
        finally
        {
            release.Set();
        }
    }

    [TestMethod]
    public void LatePreviewCannotReplaceFullImage()
    {
        using var releasePreview = new ManualResetEventSlim();
        using var previewStarted = new ManualResetEventSlim();
        using var posted = new BlockingCollection<Action>();
        using var thumbnails = new ThumbnailCoordinator(posted.Add, _ =>
        {
            previewStarted.Set();
            releasePreview.Wait();
            return CreateImage();
        });
        using var coordinator = new TestClient(posted.Add,
            new FakeImageLoadingBackend(
                () => new FakeImageDecoder(_ => CreateImage())),
            TestPolicy,
            thumbnails);
        var results = new List<ImageLoaded>();
        try
        {
            coordinator.Load("image", result => results.Add((ImageLoaded)result));
            Assert.IsTrue(previewStarted.Wait(TimeSpan.FromSeconds(5)));
            Assert.IsTrue(posted.TryTake(out Action? full, TimeSpan.FromSeconds(5)));
            full();
            releasePreview.Set();
            Assert.IsTrue(posted.TryTake(out Action? preview, TimeSpan.FromSeconds(5)));
            preview();
            Assert.HasCount(1, results);
            Assert.IsFalse(results[0].IsPreview);

        }
        finally
        {
            releasePreview.Set();
        }
    }

    [TestMethod]
    public void ThumbnailFailureDoesNotPreventFullDecode()
    {
        using var posted = new BlockingCollection<Action>();
        using var thumbnails = new ThumbnailCoordinator(posted.Add, _ =>
            throw new IOException("Thumbnail unavailable"));
        using var coordinator = new TestClient(posted.Add,
            new FakeImageLoadingBackend(
                () => new FakeImageDecoder(_ => CreateImage())),
            TestPolicy,
            thumbnails);
        ImageLoadResult? result = null;
        coordinator.Load("image", loaded => result = loaded);
        Assert.IsTrue(posted.TryTake(out Action? complete, TimeSpan.FromSeconds(5)));
        complete();
        Assert.IsInstanceOfType<ImageLoaded>(result);
        Assert.IsFalse(((ImageLoaded)result).IsPreview);
    }

    private static DecodedImage CreateImage(int width = 1, int height = 1)
    {
        int stride = checked(width * 4);
        return new DecodedImage(width, height, stride, new byte[checked(stride * height)]);
    }

    private static void DisposeResult(ImageLoadResult result)
    {
        (result as ImageLoaded)?.Dispose();
    }

    private sealed class TestClient : IDisposable
    {
        private readonly ImageLoadService _service;
        private readonly ImageLoadClient _client;

        internal TestClient(
            UiPost postToUi,
            IImageLoadingBackend backend,
            ImageRepresentationPolicy representationPolicy,
            IThumbnailLoader thumbnailLoader)
        {
            _service = new ImageLoadService(
                postToUi,
                backend,
                representationPolicy,
                thumbnailLoader);
            _client = _service.CreateClient();
        }

        internal void Load(string path, Action<ImageLoadResult> completed)
            => _client.Load(path, completed);

        internal void Preload(IEnumerable<string?> paths, Action<ImageLoadResult> completed)
            => _client.Preload(paths, completed);

        public void Dispose()
        {
            _client.Dispose();
            _service.Dispose();
        }
    }

    private sealed class FakeImageDecoder : IImageDecoder
    {
        private readonly Func<string, DecodedImage> _decode;
        private readonly Func<string, CancellationToken, DecodedImage>? _decodeWithCancellation;
        private readonly Func<string, ImageInfo> _getInfo;

        internal FakeImageDecoder(
            Func<string, DecodedImage> decode,
            Func<string, ImageInfo>? getInfo = null)
        {
            _decode = decode;
            _getInfo = getInfo ?? (_ => new ImageInfo(1, 1));
        }

        internal FakeImageDecoder(
            Func<string, CancellationToken, DecodedImage> decode,
            Func<string, ImageInfo>? getInfo = null)
        {
            _decode = _ => throw new InvalidOperationException();
            _decodeWithCancellation = decode;
            _getInfo = getInfo ?? (_ => new ImageInfo(1, 1));
        }

        public ImageInfo GetInfo(string path) => _getInfo(path);

        public DecodedImageUpload DecodeUpload(
            string path,
            CancellationToken cancellationToken = default)
        {
            DecodedImage image = _decodeWithCancellation?.Invoke(path, cancellationToken)
                ?? _decode(path);
            var upload = DecodedImageUpload.Allocate(
                image.Width,
                image.Height,
                image.Stride);
            image.Pixels.CopyTo(upload.Span);
            return upload;
        }

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

    private sealed class FakeImageLoadingBackend : IImageLoadingBackend
    {
        private readonly Func<IImageDecoder> _decoderFactory;
        private readonly IAnimatedImageDecoder? _animatedDecoder;
        private readonly Func<string, DecodedImage?> _thumbnailLoader;
        private readonly Func<string, IImageTileSource> _openTiledImage;

        internal FakeImageLoadingBackend(
            Func<IImageDecoder> decoderFactory,
            IAnimatedImageDecoder? animatedDecoder = null,
            Func<string, DecodedImage?>? thumbnailLoader = null,
            Func<string, IImageTileSource>? openTiledImage = null)
        {
            _decoderFactory = decoderFactory;
            _animatedDecoder = animatedDecoder;
            _thumbnailLoader = thumbnailLoader ?? (_ => null);
            _openTiledImage = openTiledImage
                ?? (_ => throw new NotSupportedException("Tiled images are not configured for this test."));
        }

        public IImageDecoder CreateDecoder() => _decoderFactory();

        public IImageTileSource OpenTiledImage(string path) => _openTiledImage(path);

        public DecodedImage? LoadThumbnail(string path) => _thumbnailLoader(path);

        public bool SupportsAnimation(string path) => _animatedDecoder?.CanDecode(path) == true;

        public IAnimationSession OpenAnimation(string path)
        {
            return _animatedDecoder?.Open(path)
                ?? throw new NotSupportedException("Animation is not configured for this test.");
        }
    }

    private sealed class FakeAnimatedImageDecoder : IImageDecoder, IAnimatedImageDecoder
    {
        private readonly Func<string, IAnimationSession> _open;

        internal FakeAnimatedImageDecoder(Func<string, IAnimationSession> open)
        {
            _open = open;
        }

        public bool CanDecode(string path) => true;

        public IAnimationSession Open(string path) => _open(path);

        public ImageInfo GetInfo(string path) => throw new NotSupportedException();

        public DecodedImageUpload DecodeUpload(
            string path,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class FakeAnimationSession : IAnimationSession
    {
        public AnimationFrame FirstFrame { get; } =
            new(CreateImage(), TimeSpan.FromMilliseconds(100));

        public bool IsAnimated => true;
        public bool IsComplete => false;
        public Exception? Error => null;
        public bool IsDisposed { get; private set; }

        public bool TryGetReadyFrame(out AnimationFrame frame)
        {
            frame = null!;
            return false;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class FakeTileSource : IImageTileSource
    {
        public int Width => 20_000;
        public int Height => 10_000;
        public int TileSize => 2048;
        public DecodedImage Overview { get; } = CreateImage();
        internal bool IsDisposed { get; private set; }

        public IImageTileDecoder CreateTileDecoder() => throw new NotSupportedException();

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
