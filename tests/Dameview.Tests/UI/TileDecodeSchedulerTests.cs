using System.Collections.Concurrent;
using Dameview.Imaging;
using Dameview.UI.Panels;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class TileDecodeSchedulerTests
{
    [TestMethod]
    public void ReplacesPendingViewportWorkAndUsesBoundedWorkers()
    {
        const int workerCount = 2;
        using var initialWorkersStarted = new CountdownEvent(workerCount);
        using var releaseInitialWorkers = new ManualResetEventSlim();
        var calls = new ConcurrentQueue<ImageTile>();
        int active = 0;
        int maximumActive = 0;
        var source = new FakeTileSource((tile, token) =>
        {
            int current = Interlocked.Increment(ref active);
            UpdateMaximum(ref maximumActive, current);

            calls.Enqueue(tile);
            try
            {
                if (initialWorkersStarted.CurrentCount > 0)
                {
                    initialWorkersStarted.Signal();
                    releaseInitialWorkers.Wait(token);
                }

                return CreateImage();
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });
        var posted = new BlockingCollection<Action>();
        var completed = new List<ImageTile>();
        using var scheduler = new TileDecodeScheduler(
            source,
            posted.Add,
            (tile, image) =>
            {
                Assert.IsNotNull(image);
                completed.Add(tile);
            },
            workerCount);
        ImageTile[] oldTiles = CreateTiles(0, 100);
        ImageTile[] newTiles = CreateTiles(1_000, 100);

        scheduler.ReplaceRequests(oldTiles);
        Assert.IsTrue(initialWorkersStarted.Wait(TimeSpan.FromSeconds(5)));
        scheduler.ReplaceRequests(newTiles);
        scheduler.ReplaceRequests(newTiles);
        releaseInitialWorkers.Set();

        int expectedCount = TileDecodeScheduler.MaximumPendingTiles + workerCount;
        while (completed.Count < expectedCount)
        {
            Assert.IsTrue(posted.TryTake(out Action? publish, TimeSpan.FromSeconds(5)));
            publish();
        }

        ImageTile[] decodedTiles = calls.ToArray();
        Assert.AreEqual(expectedCount, decodedTiles.Length);
        Assert.AreEqual(workerCount, maximumActive);
        Assert.AreEqual(workerCount, decodedTiles.Count(oldTiles.Contains));
        Assert.AreEqual(
            decodedTiles.Length,
            decodedTiles.Distinct().Count(),
            "A tile should only be decoded once.");
        Assert.AreEqual(workerCount, source.CreatedDecoders);
    }

    [TestMethod]
    public void FailedTileDoesNotStopLaterRequests()
    {
        ImageTile failedTile = new(0, 0, 1, 1);
        ImageTile healthyTile = new(1, 0, 1, 1);
        var calls = new ConcurrentQueue<ImageTile>();
        var source = new FakeTileSource((tile, _) =>
        {
            calls.Enqueue(tile);
            return tile == failedTile
                ? throw new InvalidDataException("Broken tile")
                : CreateImage();
        });
        var posted = new BlockingCollection<Action>();
        var completed = new List<ImageTile>();
        var failed = new List<ImageTile>();
        using var scheduler = new TileDecodeScheduler(
            source,
            posted.Add,
            (tile, image) =>
            {
                (image is null ? failed : completed).Add(tile);
            },
            maximumWorkers: 1);

        scheduler.ReplaceRequests([failedTile, healthyTile]);
        TakePostedAction(posted)();
        TakePostedAction(posted)();

        CollectionAssert.AreEqual(new[] { failedTile, healthyTile }, calls.ToArray());
        CollectionAssert.AreEqual(new[] { failedTile }, failed);
        CollectionAssert.AreEqual(new[] { healthyTile }, completed);
    }

    [TestMethod]
    public void DisposeCancelsTheActiveDecode()
    {
        using var started = new ManualResetEventSlim();
        using var canceled = new ManualResetEventSlim();
        var source = new FakeTileSource((_, token) =>
        {
            started.Set();
            try
            {
                token.WaitHandle.WaitOne();
                token.ThrowIfCancellationRequested();
                return CreateImage();
            }
            finally
            {
                canceled.Set();
            }
        });
        var scheduler = new TileDecodeScheduler(
            source,
            _ => { },
            (_, _) => { },
            maximumWorkers: 1);

        scheduler.ReplaceRequests([new ImageTile(0, 0, 1, 1)]);
        Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
        scheduler.Dispose();

        Assert.IsTrue(canceled.Wait(TimeSpan.FromSeconds(5)));
    }

    private static Action TakePostedAction(BlockingCollection<Action> posted)
    {
        Assert.IsTrue(posted.TryTake(out Action? action, TimeSpan.FromSeconds(5)));
        return action;
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        int observed = Volatile.Read(ref maximum);
        while (candidate > observed)
        {
            int previous = Interlocked.CompareExchange(ref maximum, candidate, observed);
            if (previous == observed)
            {
                return;
            }

            observed = previous;
        }
    }

    private static ImageTile[] CreateTiles(int startX, int count) =>
        Enumerable.Range(startX, count).Select(x => new ImageTile(x, 0, 1, 1)).ToArray();

    private static DecodedImage CreateImage() => new(1, 1, 4, [0, 0, 0, 255]);

    private sealed class FakeTileSource(
        Func<ImageTile, CancellationToken, DecodedImage> decode) : IImageTileSource
    {
        private readonly Func<ImageTile, CancellationToken, DecodedImage> _decode = decode;
        private int _createdDecoders;

        public int Width => 10_000;
        public int Height => 10_000;
        public int TileSize => 1;
        public DecodedImage Overview { get; } = CreateImage();
        internal int CreatedDecoders => Volatile.Read(ref _createdDecoders);

        public IImageTileDecoder CreateTileDecoder()
        {
            Interlocked.Increment(ref _createdDecoders);
            return new FakeTileDecoder(_decode);
        }

        public void Dispose()
        {
        }

        private sealed class FakeTileDecoder(
            Func<ImageTile, CancellationToken, DecodedImage> decode) : IImageTileDecoder
        {
            public DecodedImage DecodeTile(ImageTile tile, CancellationToken cancellationToken) =>
                decode(tile, cancellationToken);

            public void Dispose()
            {
            }
        }
    }
}
