using System.Collections.Concurrent;
using Dameview.Imaging;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class ImageLoadCoordinatorTests
{
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
        using var coordinator = new ImageLoadCoordinator(
            action => action(),
            () => decoder);

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
    public void DecodeFailuresAreDeliveredAsResults()
    {
        using var completed = new ManualResetEventSlim();
        ImageLoadResult? deliveredResult = null;
        using var decoder = new FakeImageDecoder(
            _ => throw new InvalidDataException("Broken image"));
        using var coordinator = new ImageLoadCoordinator(
            action => action(),
            () => decoder);

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
        using var coordinator = new ImageLoadCoordinator(
            action =>
            {
                postedActions.Enqueue(action);
                resultPosted.Release();
            },
            () => decoder);

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
        using var coordinator = new ImageLoadCoordinator(action => action(), () => decoder);

        try
        {
            coordinator.Load("first.gif", _ => Assert.Fail("Superseded result was delivered."));
            Assert.IsTrue(firstStarted.Wait(TimeSpan.FromSeconds(5)));

            coordinator.Load("second.gif", result =>
            {
                ((ImageLoaded)result).Animation!.Dispose();
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
    public void PreloadedImageIsServedWithoutForegroundDecoding()
    {
        using var sentinelStarted = new ManualResetEventSlim();
        using var releaseSentinel = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        var decodeCounts = new ConcurrentDictionary<string, int>();
        DecodedImage Decode(string path)
        {
            decodeCounts.AddOrUpdate(path, 1, static (_, count) => count + 1);
            if (path == "sentinel")
            {
                sentinelStarted.Set();
                releaseSentinel.Wait();
            }

            return CreateImage();
        }

        using var coordinator = new ImageLoadCoordinator(
            action => action(),
            () => new FakeImageDecoder(Decode));

        try
        {
            coordinator.Preload(["next", "sentinel"]);
            Assert.IsTrue(sentinelStarted.Wait(TimeSpan.FromSeconds(5)));

            coordinator.Load("next", _ => completed.Set());

            Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
            Assert.AreEqual(1, decodeCounts["next"]);
        }
        finally
        {
            releaseSentinel.Set();
        }
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

        using var coordinator = new ImageLoadCoordinator(
            action => action(),
            () => new FakeImageDecoder(Decode));

        try
        {
            coordinator.Preload(["same"]);
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
        using var coordinator = new ImageLoadCoordinator(posted.Add,
            () => new FakeImageDecoder(_ => { release.Wait(); return CreateImage(); }),
            thumbnailLoader: _ => CreateImage());
        var results = new List<ImageLoaded>();
        try
        {
            coordinator.Load("image", result => results.Add((ImageLoaded)result));
            Assert.IsTrue(posted.TryTake(out Action? preview, TimeSpan.FromSeconds(5)));
            preview();
            Assert.IsTrue(results[0].IsPreview);
            release.Set();
            Assert.IsTrue(posted.TryTake(out Action? full, TimeSpan.FromSeconds(5)));
            full();
            Assert.HasCount(2, results);
            Assert.IsFalse(results[1].IsPreview);
        }
        finally
        {
            release.Set();
        }
    }

    [TestMethod]
    public void LatePreviewCannotReplaceFullImageOrANewerRequest()
    {
        using var releasePreview = new ManualResetEventSlim();
        using var previewStarted = new ManualResetEventSlim();
        using var posted = new BlockingCollection<Action>();
        using var coordinator = new ImageLoadCoordinator(posted.Add,
            () => new FakeImageDecoder(_ => CreateImage()),
            thumbnailLoader: _ =>
            {
                previewStarted.Set();
                releasePreview.Wait();
                return CreateImage();
            });
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

            coordinator.Load("image", result => results.Add((ImageLoaded)result));
            preview();
            Assert.HasCount(1, results);
            Assert.IsTrue(posted.TryTake(out Action? cached, TimeSpan.FromSeconds(5)));
            cached();
            Assert.HasCount(2, results);
            Assert.IsFalse(results[1].IsPreview);
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
        using var coordinator = new ImageLoadCoordinator(posted.Add,
            () => new FakeImageDecoder(_ => CreateImage()),
            thumbnailLoader: _ => throw new IOException("Thumbnail unavailable"));
        ImageLoadResult? result = null;
        coordinator.Load("image", loaded => result = loaded);
        Assert.IsTrue(posted.TryTake(out Action? complete, TimeSpan.FromSeconds(5)));
        complete();
        Assert.IsInstanceOfType<ImageLoaded>(result);
        Assert.IsFalse(((ImageLoaded)result).IsPreview);
    }

    private static DecodedImage CreateImage()
    {
        return new DecodedImage(1, 1, 4, new byte[4]);
    }

    private sealed class FakeImageDecoder : IImageDecoder
    {
        private readonly Func<string, DecodedImage> _decode;

        internal FakeImageDecoder(Func<string, DecodedImage> decode)
        {
            _decode = decode;
        }

        public DecodedImage Decode(string path)
        {
            return _decode(path);
        }

        public void Dispose()
        {
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

        public DecodedImage Decode(string path) => throw new NotSupportedException();

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
        public bool IsInfiniteLoop => true;
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
}
