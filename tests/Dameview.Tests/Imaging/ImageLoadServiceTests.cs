using System.Collections.Concurrent;
using Dameview.Imaging;
using Dameview.Imaging.Animation;
using Dameview.Imaging.Decoding;
using Dameview.Imaging.Loading;
using Dameview.Win32;

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
            new WindowSynchronizationContext(action => action()),
            new FakeImageLoadingBackend(() => new FakeImageDecoder(path =>
            {
                if (path == "first")
                {
                    firstStarted.Set();
                    releaseFirst.Wait();
                }

                return CreateImage();
            })),
            TestPolicy);
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
            new WindowSynchronizationContext(action => action()),
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
            TestPolicy);
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
            TestPolicy);

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
            TestPolicy);

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
            TestPolicy);

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
            TestPolicy);

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
            TestPolicy);

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
    [DataRow("large.png")]
    [DataRow("large.webp")]
    [DataRow("large.gif")]
    public void ForegroundUsesTiledRepresentationWithoutFullDecode(string path)
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
                    _ => new ImageInfo(20_000, 10_000, 1)),
                animatedDecoder: new FakeAnimatedImageDecoder(_ => throw new AssertFailedException("Static image opened as animation.")),
                openTiledImage: _ => tiles),
            TestPolicy);
        ImageLoaded? loaded = null;

        coordinator.Load(path, result =>
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
                        ? new ImageInfo(20_000, 10_000, 1)
                        : new ImageInfo(1, 1, 1)),
                openTiledImage: path =>
                {
                    openedTilePaths.Enqueue(path);
                    return new FakeTileSource();
                }),
            TestPolicy);

        coordinator.Preload(["large", "sentinel"], DisposeResult);

        Assert.IsTrue(sentinelDecoded.Wait(TimeSpan.FromSeconds(5)));
        CollectionAssert.AreEqual(new[] { "sentinel" }, decodedPaths.ToArray());
        Assert.IsEmpty(openedTilePaths);
    }

    [TestMethod]
    [DataRow("next.png")]
    [DataRow("next.webp")]
    [DataRow("next.gif")]
    public void CompletedPreloadDeliversDisposableUpload(string path)
    {
        using var completed = new ManualResetEventSlim();
        using var coordinator = new TestClient(
            action => action(),
            new FakeImageLoadingBackend(
                () => new FakeImageDecoder(_ => CreateImage()),
                new FakeAnimatedImageDecoder(_ => throw new AssertFailedException("Static image opened as animation."))),
            TestPolicy);
        ImageLoaded? loaded = null;

        coordinator.Preload([path], result =>
        {
            loaded = (ImageLoaded)result;
            completed.Set();
        });

        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsInstanceOfType<UploadImageRepresentation>(loaded!.Representation);
        loaded.Dispose();
    }

    [TestMethod]
    [DataRow("same.png")]
    [DataRow("same.webp")]
    [DataRow("same.gif")]
    public void ForegroundLoadJoinsAnActivePreloadForTheSameImage(string path)
    {
        using var decodeStarted = new ManualResetEventSlim();
        using var releaseDecode = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        using var preloadCompleted = new ManualResetEventSlim();
        ImageLoaded? preload = null;
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
            new FakeImageLoadingBackend(
                () => new FakeImageDecoder(Decode),
                new FakeAnimatedImageDecoder(_ => throw new AssertFailedException("Static image opened as animation."))),
            TestPolicy);

        try
        {
            coordinator.Preload([path], result =>
            {
                preload = (ImageLoaded)result;
                preloadCompleted.Set();
            });
            Assert.IsTrue(decodeStarted.Wait(TimeSpan.FromSeconds(5)));

            coordinator.Load(path, result =>
            {
                DisposeResult(result);
                completed.Set();
            });
            releaseDecode.Set();

            Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
            Assert.IsTrue(preloadCompleted.Wait(TimeSpan.FromSeconds(5)));
            Assert.AreEqual(1, Volatile.Read(ref decodeCount));
        }
        finally
        {
            releaseDecode.Set();
            preloadCompleted.Wait(TimeSpan.FromSeconds(5));
            preload?.Dispose();
        }
    }

    [TestMethod]
    public void MetadataFailureFailsForegroundLoad()
    {
        using var completed = new ManualResetEventSlim();
        using var coordinator = new TestClient(
            action => action(),
            new FakeImageLoadingBackend(
                () => new FakeImageDecoder(
                    _ => CreateImage(),
                    _ => throw new IOException("Broken header"))),
            TestPolicy);
        ImageLoadResult? result = null;

        coordinator.Load("image", loaded =>
        {
            result = loaded;
            completed.Set();
        });

        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsInstanceOfType<ImageLoadFailed>(result);
    }

    [TestMethod]
    [DataRow(1, true, false)]
    [DataRow(2, true, true)]
    [DataRow(2, false, false)]
    public void ForegroundRequiresMultipleFramesAndAnAnimationDecoder(
        int frameCount, bool supported, bool animated)
    {
        using var completed = new ManualResetEventSlim();
        var session = new FakeAnimationSession();
        using var coordinator = new TestClient(
            action => action(),
            new FakeImageLoadingBackend(
                () => new FakeImageDecoder(_ => CreateImage(), _ => new ImageInfo(1, 1, frameCount)),
                supported ? new FakeAnimatedImageDecoder(_ => session) : null),
            TestPolicy);
        ImageLoadResult? result = null;
        coordinator.Load("image", value =>
        {
            result = value;
            completed.Set();
        });

        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsInstanceOfType<ImageLoaded>(result);
        using var loaded = (ImageLoaded)result;
        if (animated)
        {
            Assert.IsInstanceOfType<AnimatedImageRepresentation>(loaded.Representation);
        }
        else
        {
            Assert.IsInstanceOfType<UploadImageRepresentation>(loaded.Representation);
        }
    }

    [TestMethod]
    public void PreloadSkipsAnimatedContentButLoadsStaticFilesOfTheSameFormat()
    {
        using var completed = new ManualResetEventSlim();
        var decodedPaths = new ConcurrentQueue<string>();
        using var coordinator = new TestClient(
            action => action(),
            new FakeImageLoadingBackend(
                () => new FakeImageDecoder(
                    path =>
                    {
                        decodedPaths.Enqueue(path);
                        return CreateImage();
                    },
                    path => new ImageInfo(1, 1, path.StartsWith("animated", StringComparison.Ordinal) ? 2 : 1)),
                new FakeAnimatedImageDecoder(_ => throw new AssertFailedException("Preload opened animation."))),
            TestPolicy);
        coordinator.Preload(["animated.webp", "animated.gif", "static.webp", "static.gif"], result =>
        {
            DisposeResult(result);
            if (result.Path == "static.gif")
            {
                completed.Set();
            }
        });

        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
        CollectionAssert.AreEqual(new[] { "static.webp", "static.gif" }, decodedPaths.ToArray());
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
            Action<Action> postToUi,
            IImageLoadingBackend backend,
            ImageRepresentationPolicy representationPolicy)
        {
            _service = new ImageLoadService(
                new WindowSynchronizationContext(postToUi),
                backend,
                representationPolicy);
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
            _getInfo = getInfo ?? (_ => new ImageInfo(1, 1, 1));
        }

        internal FakeImageDecoder(
            Func<string, CancellationToken, DecodedImage> decode,
            Func<string, ImageInfo>? getInfo = null)
        {
            _decode = _ => throw new InvalidOperationException();
            _decodeWithCancellation = decode;
            _getInfo = getInfo ?? (_ => new ImageInfo(1, 1, 1));
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

    private sealed class FakeImageLoadingBackend : IImageLoadingBackend
    {
        private readonly Func<IImageDecoder> _decoderFactory;
        private readonly IAnimatedImageDecoder? _animatedDecoder;
        private readonly Func<string, DecodedImageUpload?> _thumbnailLoader;
        private readonly Func<string, IImageTileSource> _openTiledImage;

        internal FakeImageLoadingBackend(
            Func<IImageDecoder> decoderFactory,
            IAnimatedImageDecoder? animatedDecoder = null,
            Func<string, DecodedImageUpload?>? thumbnailLoader = null,
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

        public DecodedImageUpload? LoadThumbnail(string path) => _thumbnailLoader(path);

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

        public ImageInfo GetInfo(string path) => new(1, 1, 2);

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
