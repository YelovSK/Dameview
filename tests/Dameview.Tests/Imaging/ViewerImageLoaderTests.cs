using Dameview.Imaging.Loading;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class ViewerImageLoaderTests
{
    [TestMethod]
    public void PreviewArrivesBeforeTheFullImage()
    {
        using var release = new ManualResetEventSlim();
        using var harness = new PipelineHarness(
            new FakeSource(_ =>
            {
                release.Wait();
                return PipelineHarness.CreateImage(9, 9);
            }),
            new FakeSource(_ => PipelineHarness.CreateImage(512, 256)));
        using var loader = new ViewerImageLoader(harness.Pipeline);
        var results = new List<ImageLoaded>();

        try
        {
            loader.Load("image", result => results.Add((ImageLoaded)result));
            harness.PumpUntil(() => results.Count == 1);

            Assert.IsTrue(results[0].IsPreview);
            Assert.AreEqual(512, results[0].Representation.Width);
            Assert.AreEqual(256, results[0].Representation.Height);

            release.Set();
            harness.PumpUntil(() => results.Count == 2);

            Assert.IsFalse(results[1].IsPreview);
            Assert.AreEqual(9, results[1].Representation.Width);
        }
        finally
        {
            release.Set();
            results.ForEach(result => result.Dispose());
        }
    }

    [TestMethod]
    public void LatePreviewIsDroppedOnceTheFullImageArrives()
    {
        using var releasePreview = new ManualResetEventSlim();
        using var harness = new PipelineHarness(
            new FakeSource(_ => PipelineHarness.CreateImage()),
            new FakeSource(_ =>
            {
                releasePreview.Wait();
                return PipelineHarness.CreateImage(512, 512);
            }));
        using var loader = new ViewerImageLoader(harness.Pipeline);
        var results = new List<ImageLoaded>();

        try
        {
            loader.Load("image", result => results.Add((ImageLoaded)result));
            harness.PumpUntil(() => results.Count == 1);
            releasePreview.Set();
            harness.PumpPending();

            Assert.HasCount(1, results);
            Assert.IsFalse(results[0].IsPreview);
        }
        finally
        {
            releasePreview.Set();
            results.ForEach(result => result.Dispose());
        }
    }

    [TestMethod]
    public void NewerLoadReplacesAnOlderOne()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        var source = new FakeSource(
            path =>
            {
                if (path == "first")
                {
                    firstStarted.Set();
                    releaseFirst.Wait();
                }

                return PipelineHarness.CreateImage();
            },
            workerCount: 2);
        using var harness = new PipelineHarness(source);
        using var loader = new ViewerImageLoader(harness.Pipeline);
        var delivered = new List<ImageLoadResult>();

        loader.Load("first", delivered.Add);
        Assert.IsTrue(firstStarted.Wait(TimeSpan.FromSeconds(5)));
        loader.Load("second", delivered.Add);
        harness.PumpUntil(() => delivered.Count == 1);
        releaseFirst.Set();
        harness.PumpPending();

        Assert.HasCount(1, delivered);
        Assert.AreEqual("second", delivered[0].Path);
        ((ImageLoaded)delivered[0]).Dispose();
    }

    [TestMethod]
    public void SecondLoadUsesTheCacheWithoutLoadingAgain()
    {
        var source = new FakeSource(_ => PipelineHarness.CreateImage());
        using var harness = new PipelineHarness(source);
        using var loader = new ViewerImageLoader(harness.Pipeline);
        ImageLoaded? first = null;
        loader.Load("image", result => first = (ImageLoaded)result);
        harness.PumpUntil(() => first is not null);
        first!.Dispose();

        ImageLoaded? second = null;
        loader.Load("image", result => second = (ImageLoaded)result);

        Assert.IsNotNull(second);
        Assert.IsInstanceOfType<CachedBitmapLease>(second.Representation);
        CollectionAssert.AreEqual(new[] { "image" }, source.Loaded);
        second.Dispose();
    }

    [TestMethod]
    public void ReplacingPreloadsKeepsLoadingTheImagesStillWanted()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var source = new FakeSource(
            path =>
            {
                if (path == "kept")
                {
                    started.Set();
                    release.Wait();
                }

                return PipelineHarness.CreateImage();
            },
            workerCount: 2);
        using var harness = new PipelineHarness(source);
        using var loader = new ViewerImageLoader(harness.Pipeline);

        loader.Preload(["kept"]);
        Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
        loader.Preload(["kept", "added"]);
        release.Set();
        harness.PumpPending();

        CollectionAssert.AreEquivalent(new[] { "kept", "added" }, source.Loaded);
    }
}
