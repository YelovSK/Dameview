using System.Collections.Concurrent;
using Dameview.Imaging.Loading;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class ImagePipelineTests
{
    [TestMethod]
    public void RequestsForTheSameImageShareOneLoad()
    {
        using var release = new ManualResetEventSlim();
        var source = new FakeSource(_ =>
        {
            release.Wait();
            return PipelineHarness.CreateImage();
        });
        using var harness = new PipelineHarness(source);
        var results = new List<ImageLoadResult>();

        using IDisposable first = harness.Pipeline.Request(Full("image"), ImagePriority.Display, results.Add);
        using IDisposable second = harness.Pipeline.Request(Full("image"), ImagePriority.Gallery, results.Add);
        release.Set();
        harness.PumpUntil(() => results.Count == 2);

        CollectionAssert.AreEqual(new[] { "image" }, source.Loaded);
        foreach (ImageLoadResult result in results)
        {
            Assert.IsInstanceOfType<CachedBitmapLease>(((ImageLoaded)result).Representation);
            ((ImageLoaded)result).Dispose();
        }
    }

    [TestMethod]
    public void CachedImageIsDeliveredBeforeRequestReturns()
    {
        var source = new FakeSource(_ => PipelineHarness.CreateImage());
        using var harness = new PipelineHarness(source);
        ImageLoaded? first = null;
        using IDisposable request = harness.Pipeline.Request(
            Full("image"),
            ImagePriority.Display,
            result => first = (ImageLoaded)result);
        harness.PumpUntil(() => first is not null);
        first!.Dispose();

        ImageLoaded? second = null;
        using IDisposable again = harness.Pipeline.Request(
            Full("IMAGE"),
            ImagePriority.Display,
            result => second = (ImageLoaded)result);

        Assert.IsNotNull(second);
        CollectionAssert.AreEqual(new[] { "image" }, source.Loaded);
        second.Dispose();
    }

    [TestMethod]
    public void MoreUrgentRequestMovesAQueuedLoadAhead()
    {
        using var blockerStarted = new ManualResetEventSlim();
        using var releaseBlocker = new ManualResetEventSlim();
        var source = new FakeSource(path =>
        {
            if (path == "blocker")
            {
                blockerStarted.Set();
                releaseBlocker.Wait();
            }

            return PipelineHarness.CreateImage();
        });
        using var harness = new PipelineHarness(source);
        var delivered = new List<ImageLoadResult>();

        using IDisposable blocker = harness.Pipeline.Request(Full("blocker"), ImagePriority.Display, delivered.Add);
        Assert.IsTrue(blockerStarted.Wait(TimeSpan.FromSeconds(5)));
        using IDisposable earlier = harness.Pipeline.Request(Full("earlier"), ImagePriority.Gallery, delivered.Add);
        using IDisposable gallery = harness.Pipeline.Request(Full("promoted"), ImagePriority.Gallery, delivered.Add);
        using IDisposable display = harness.Pipeline.Request(Full("promoted"), ImagePriority.Display, delivered.Add);
        releaseBlocker.Set();
        harness.PumpUntil(() => delivered.Count == 4);

        CollectionAssert.AreEqual(new[] { "blocker", "promoted", "earlier" }, source.Loaded);
        DisposeAll(delivered);
    }

    [TestMethod]
    public void DisposedRequestIsNotDelivered()
    {
        using var harness = new PipelineHarness(new FakeSource(_ => PipelineHarness.CreateImage()));
        bool delivered = false;

        harness.Pipeline.Request(Full("image"), ImagePriority.Display, _ => delivered = true).Dispose();
        harness.PumpPending();

        Assert.IsFalse(delivered);
    }

    [TestMethod]
    public void DisposedQueuedRequestIsSkippedBeforeLoading()
    {
        using var blockerStarted = new ManualResetEventSlim();
        using var releaseBlocker = new ManualResetEventSlim();
        var source = new FakeSource(path =>
        {
            if (path == "blocker")
            {
                blockerStarted.Set();
                releaseBlocker.Wait();
            }

            return PipelineHarness.CreateImage();
        });
        using var harness = new PipelineHarness(source);
        var delivered = new List<ImageLoadResult>();

        using IDisposable blocker = harness.Pipeline.Request(Full("blocker"), ImagePriority.Display, delivered.Add);
        Assert.IsTrue(blockerStarted.Wait(TimeSpan.FromSeconds(5)));
        harness.Pipeline.Request(Full("stale"), ImagePriority.Display, delivered.Add).Dispose();
        using IDisposable current = harness.Pipeline.Request(Full("current"), ImagePriority.Display, delivered.Add);
        releaseBlocker.Set();
        harness.PumpUntil(() => delivered.Count == 2);

        CollectionAssert.AreEqual(new[] { "blocker", "current" }, source.Loaded);
        DisposeAll(delivered);
    }

    [TestMethod]
    public void FailureIsDeliveredAndTheImageCanBeRequestedAgain()
    {
        int loads = 0;
        using var harness = new PipelineHarness(new FakeSource(_ => ++loads == 1
            ? throw new IOException("Broken header")
            : PipelineHarness.CreateImage()));
        var delivered = new List<ImageLoadResult>();

        using IDisposable first = harness.Pipeline.Request(Full("image"), ImagePriority.Display, delivered.Add);
        harness.PumpUntil(() => delivered.Count == 1);
        using IDisposable second = harness.Pipeline.Request(Full("image"), ImagePriority.Display, delivered.Add);
        harness.PumpUntil(() => delivered.Count == 2);

        Assert.IsInstanceOfType<ImageLoadFailed>(delivered[0]);
        Assert.IsInstanceOfType<ImageLoaded>(delivered[1]);
        DisposeAll(delivered);
    }

    [TestMethod]
    public void DisplayRequestJoinsARunningPreload()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var source = new FakeSource(_ =>
        {
            started.Set();
            release.Wait();
            return PipelineHarness.CreateImage();
        });
        using var harness = new PipelineHarness(source);
        var delivered = new List<ImageLoadResult>();

        using IDisposable preload = harness.Pipeline.Request(Full("image"), ImagePriority.Preload, delivered.Add);
        Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
        using IDisposable display = harness.Pipeline.Request(Full("image"), ImagePriority.Display, delivered.Add);
        release.Set();
        harness.PumpUntil(() => delivered.Count == 2);

        CollectionAssert.AreEqual(new[] { "image" }, source.Loaded);
        DisposeAll(delivered);
    }

    [TestMethod]
    public void LoadNobodyWantsAnymoreIsNotUploaded()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var source = new FakeSource(_ =>
        {
            started.Set();
            release.Wait();
            return PipelineHarness.CreateImage();
        });
        using var harness = new PipelineHarness(source);

        IDisposable request = harness.Pipeline.Request(Full("image"), ImagePriority.Display, _ => { });
        Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
        request.Dispose();
        release.Set();
        harness.PumpPending();

        Assert.AreEqual(0, harness.Uploader.Uploads);
    }

    [TestMethod]
    public void PreloadsRunOnAWorkerOfTheirOwn()
    {
        using var preloadStarted = new ManualResetEventSlim();
        using var releasePreload = new ManualResetEventSlim();
        var source = new FakeSource(path =>
        {
            if (path == "preload")
            {
                preloadStarted.Set();
                releasePreload.Wait();
            }

            return PipelineHarness.CreateImage();
        });
        using var harness = new PipelineHarness(source);
        var delivered = new List<ImageLoadResult>();

        try
        {
            using IDisposable preload = harness.Pipeline.Request(Full("preload"), ImagePriority.Preload, delivered.Add);
            Assert.IsTrue(preloadStarted.Wait(TimeSpan.FromSeconds(5)));
            using IDisposable display = harness.Pipeline.Request(Full("display"), ImagePriority.Display, delivered.Add);
            harness.PumpUntil(() => delivered.Count == 1);

            Assert.AreEqual("display", delivered[0].Path);
        }
        finally
        {
            releasePreload.Set();
            DisposeAll(delivered);
        }
    }

    [TestMethod]
    public void PreloadsThatWouldNotFitAreSkippedBeforeDecoding()
    {
        var decoded = new ConcurrentQueue<string>();
        var source = new FileImageSource(
            new FakeImageLoadingBackend(() => new FakeImageDecoder(
                path =>
                {
                    decoded.Enqueue(path);
                    return FakeImageDecoder.CreateImage();
                },
                path => path == "small" ? new ImageInfo(1, 1, 1) : new ImageInfo(10, 10, 1))),
            PipelineHarness.TestPolicy);
        // 400 bytes for each of the large two and 4 for the small one, so only one large one fits.
        using var harness = new PipelineHarness(source, fullCacheBytes: 404);
        var delivered = new List<ImageLoadResult>();

        IDisposable[] preloads =
        [
            harness.Pipeline.Request(Full("first"), ImagePriority.Preload, delivered.Add),
            harness.Pipeline.Request(Full("second"), ImagePriority.Preload, delivered.Add),
            harness.Pipeline.Request(Full("small"), ImagePriority.Preload, delivered.Add),
        ];
        harness.PumpUntil(() => delivered.Count == 2);
        harness.PumpPending();

        Assert.HasCount(2, delivered);
        Assert.HasCount(2, decoded);
        CollectionAssert.Contains(decoded.ToArray(), "small");
        DisposeAll(delivered);
        Array.ForEach(preloads, preload => preload.Dispose());
    }

    [TestMethod]
    public void PreloadSkipsImagesThatRequireTiling()
    {
        var decoded = new ConcurrentQueue<string>();
        var source = new FileImageSource(
            new FakeImageLoadingBackend(
                () => new FakeImageDecoder(
                    path =>
                    {
                        decoded.Enqueue(path);
                        return FakeImageDecoder.CreateImage();
                    },
                    path => path == "large" ? new ImageInfo(20_000, 10_000, 1) : new ImageInfo(1, 1, 1)),
                openTiledImage: _ => throw new AssertFailedException("Opened a tiled image for a preload.")),
            PipelineHarness.TestPolicy);
        using var harness = new PipelineHarness(source);
        var delivered = new List<ImageLoadResult>();

        using IDisposable large = harness.Pipeline.Request(Full("large"), ImagePriority.Preload, delivered.Add);
        using IDisposable small = harness.Pipeline.Request(Full("small"), ImagePriority.Preload, delivered.Add);
        harness.PumpUntil(() => delivered.Count == 1);
        harness.PumpPending();

        Assert.HasCount(1, delivered);
        CollectionAssert.AreEqual(new[] { "small" }, decoded.ToArray());
        DisposeAll(delivered);
    }

    [TestMethod]
    public void ImageIsLoadedAgainWhenTheDeviceChangesRightAfterItsUpload()
    {
        var uploader = new FakeUploader();
        uploader.SwitchDeviceAfterNextUploads(1);
        var source = new FakeSource(_ => PipelineHarness.CreateImage());
        using var harness = new PipelineHarness(source, uploader: uploader);
        ImageLoaded? loaded = null;

        using IDisposable request = harness.Pipeline.Request(
            Full("image"),
            ImagePriority.Display,
            result => loaded = (ImageLoaded)result);
        harness.PumpUntil(() => loaded is not null);

        Assert.AreEqual(2, uploader.Uploads);
        Assert.HasCount(2, source.Loaded);
        loaded!.Dispose();
    }

    [TestMethod]
    public void TiledImageGoesToTheMostUrgentRequestAndNotToAPreload()
    {
        using var release = new ManualResetEventSlim();
        var source = new FakeSource(_ =>
        {
            release.Wait();
            return new TiledImageRepresentation(new FakeTileSource());
        });
        using var harness = new PipelineHarness(source);
        var preloaded = new List<ImageLoadResult>();
        var displayed = new List<ImageLoadResult>();

        using IDisposable preload = harness.Pipeline.Request(Full("large"), ImagePriority.Preload, preloaded.Add);
        using IDisposable display = harness.Pipeline.Request(Full("large"), ImagePriority.Display, displayed.Add);
        release.Set();
        harness.PumpUntil(() => displayed.Count == 1);
        harness.PumpPending();

        Assert.IsInstanceOfType<TiledImageRepresentation>(((ImageLoaded)displayed[0]).Representation);
        Assert.IsEmpty(preloaded);
        Assert.HasCount(1, source.Loaded);
        DisposeAll(displayed);
    }

    [TestMethod]
    public void TiledImageGoesToOneRequestAndTheOtherLoadsItsOwn()
    {
        using var release = new ManualResetEventSlim();
        var source = new FakeSource(_ =>
        {
            release.Wait();
            return new TiledImageRepresentation(new FakeTileSource());
        });
        using var harness = new PipelineHarness(source);
        var delivered = new List<ImageLoadResult>();

        using IDisposable first = harness.Pipeline.Request(Full("large"), ImagePriority.Display, delivered.Add);
        using IDisposable second = harness.Pipeline.Request(Full("large"), ImagePriority.Display, delivered.Add);
        release.Set();
        harness.PumpUntil(() => delivered.Count == 2);

        Assert.HasCount(2, source.Loaded);
        Assert.AreNotSame(((ImageLoaded)delivered[0]).Representation, ((ImageLoaded)delivered[1]).Representation);
        DisposeAll(delivered);
    }

    private static ImageKey Full(string path) => new(path, ImageVariant.Full);

    private static void DisposeAll(IEnumerable<ImageLoadResult> results)
    {
        foreach (ImageLoadResult result in results)
        {
            (result as ImageLoaded)?.Dispose();
        }
    }
}
