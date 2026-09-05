using System.Collections.Concurrent;
using Dameview.Imaging;

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
        using var coordinator = new ThumbnailCoordinator(posted.Add, _ =>
        {
            Interlocked.Increment(ref loadCount);
            started.Set();
            release.Wait();
            return CreateImage();
        });
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
    public void CachedThumbnailDoesNotLoadAgain()
    {
        using var posted = new BlockingCollection<Action>();
        int loadCount = 0;
        using var coordinator = new ThumbnailCoordinator(posted.Add, _ =>
        {
            Interlocked.Increment(ref loadCount);
            return CreateImage();
        });

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
        using var coordinator = new ThumbnailCoordinator(posted.Add, _ => CreateImage());
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

    private static DecodedImage CreateImage() => new(1, 1, 4, new byte[4]);
}
