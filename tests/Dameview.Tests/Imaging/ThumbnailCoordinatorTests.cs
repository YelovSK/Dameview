using System.Collections.Concurrent;
using Dameview.Imaging;
using Dameview.Platform;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class ThumbnailCoordinatorTests
{
    [TestMethod]
    public void RequestsForTheSamePathShareOneLoadAndBothReceiveTheImage()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var posted = new BlockingCollection<Action>();
        int loadCount = 0;
        using var coordinator = new ThumbnailCoordinator(
            _ =>
            {
                Interlocked.Increment(ref loadCount);
                started.Set();
                release.Wait();
                return CreateImage();
            },
            new WindowSynchronizationContext(posted.Add));
        int completed = 0;

        using IDisposable gallery = coordinator.Request(
            "same.jpg",
            ThumbnailPriority.Gallery,
            _ => completed++);
        Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
        using IDisposable foreground = coordinator.Request(
            "same.jpg",
            ThumbnailPriority.Foreground,
            _ => completed++);
        release.Set();

        Assert.IsTrue(posted.TryTake(out Action? first, TimeSpan.FromSeconds(5)));
        Assert.IsTrue(posted.TryTake(out Action? second, TimeSpan.FromSeconds(5)));
        first();
        second();
        Assert.AreEqual(1, Volatile.Read(ref loadCount));
        Assert.AreEqual(2, completed);
    }

    [TestMethod]
    public void ForegroundRequestPromotesPendingGalleryThumbnail()
    {
        using var blockerStarted = new ManualResetEventSlim();
        using var releaseBlocker = new ManualResetEventSlim();
        using var loaded = new CountdownEvent(2);
        using var posted = new BlockingCollection<Action>();
        var loadOrder = new ConcurrentQueue<string>();
        using var coordinator = new ThumbnailCoordinator(
            path =>
            {
                if (path == "blocker.jpg")
                {
                    blockerStarted.Set();
                    releaseBlocker.Wait();
                }
                else
                {
                    loadOrder.Enqueue(path);
                    loaded.Signal();
                }

                return CreateImage();
            },
            new WindowSynchronizationContext(posted.Add));

        using IDisposable blocker = coordinator.Request(
            "blocker.jpg",
            ThumbnailPriority.Foreground,
            _ => { });
        Assert.IsTrue(blockerStarted.Wait(TimeSpan.FromSeconds(5)));
        using IDisposable earlierGallery = coordinator.Request(
            "earlier.jpg",
            ThumbnailPriority.Gallery,
            _ => { });
        using IDisposable gallery = coordinator.Request(
            "promoted.jpg",
            ThumbnailPriority.Gallery,
            _ => { });
        using IDisposable foreground = coordinator.Request(
            "promoted.jpg",
            ThumbnailPriority.Foreground,
            _ => { });
        releaseBlocker.Set();

        Assert.IsTrue(loaded.Wait(TimeSpan.FromSeconds(5)));
        for (int index = 0; index < 4; index++)
        {
            Assert.IsTrue(posted.TryTake(out Action? delivery, TimeSpan.FromSeconds(5)));
            delivery();
        }

        string[] expected = ["promoted.jpg", "earlier.jpg"];
        CollectionAssert.AreEqual(expected, loadOrder.ToArray());
    }

    [TestMethod]
    public void CachedThumbnailDoesNotLoadAgain()
    {
        using var posted = new BlockingCollection<Action>();
        int loadCount = 0;
        using var coordinator = new ThumbnailCoordinator(
            _ =>
            {
                Interlocked.Increment(ref loadCount);
                return CreateImage();
            },
            new WindowSynchronizationContext(posted.Add));

        using IDisposable firstRequest = coordinator.Request(
            "cached.jpg",
            ThumbnailPriority.Gallery,
            _ => { });
        Assert.IsTrue(posted.TryTake(out Action? first, TimeSpan.FromSeconds(5)));
        first();
        using IDisposable secondRequest = coordinator.Request(
            "cached.jpg",
            ThumbnailPriority.Foreground,
            _ => { });
        Assert.IsTrue(posted.TryTake(out Action? second, TimeSpan.FromSeconds(5)));
        second();

        Assert.AreEqual(1, Volatile.Read(ref loadCount));
    }

    [TestMethod]
    public void CancelledRequestIsNotDelivered()
    {
        using var posted = new BlockingCollection<Action>();
        using var coordinator = new ThumbnailCoordinator(
            _ => CreateImage(),
            new WindowSynchronizationContext(posted.Add));
        bool delivered = false;
        IDisposable request = coordinator.Request(
            "cancelled.jpg",
            ThumbnailPriority.Gallery,
            _ => delivered = true);
        Assert.IsTrue(posted.TryTake(out Action? delivery, TimeSpan.FromSeconds(5)));
        request.Dispose();
        delivery();
        Assert.IsFalse(delivered);
    }

    [TestMethod]
    public void FailedThumbnailCanBeRequestedAgain()
    {
        using var posted = new BlockingCollection<Action>();
        int loadCount = 0;
        using var coordinator = new ThumbnailCoordinator(
            _ => Interlocked.Increment(ref loadCount) == 1 ? null : CreateImage(),
            new WindowSynchronizationContext(posted.Add));
        using IDisposable firstRequest = coordinator.Request(
            "retry.jpg",
            ThumbnailPriority.Gallery,
            _ => { });
        Assert.IsTrue(SpinWait.SpinUntil(
            () => Volatile.Read(ref loadCount) == 1,
            TimeSpan.FromSeconds(5)));

        var retryRequests = new List<IDisposable>();
        Action? delivery = null;
        for (int attempt = 0; attempt < 100 && delivery is null; attempt++)
        {
            retryRequests.Add(coordinator.Request(
                "retry.jpg",
                ThumbnailPriority.Foreground,
                _ => { }));
            _ = posted.TryTake(out delivery, TimeSpan.FromMilliseconds(50));
        }

        foreach (IDisposable request in retryRequests)
        {
            request.Dispose();
        }

        Assert.IsNotNull(delivery);
        Assert.AreEqual(2, Volatile.Read(ref loadCount));
    }

    private static DecodedImage CreateImage() => new(1, 1, 4, new byte[4]);
}
