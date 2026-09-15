using System.Collections.Concurrent;
using Dameview.Imaging;
using Dameview.Imaging.Loading;
using Dameview.UI.Presentation;
using Dameview.Win32;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class ThumbnailImageLoaderTests
{
    [TestMethod]
    public void DeliveredThumbnailIsCachedAndSharedWithoutLoadingAgain()
    {
        using var posted = new BlockingCollection<Action>();
        var source = new FakeThumbnailSource();
        using var cache = new RenderBitmapCache(1024, _ => { });
        var loader = new ThumbnailImageLoader(
            source,
            cache,
            new WindowSynchronizationContext(posted.Add),
            _ => null!);
        CachedBitmapLease? first = null;

        using IDisposable firstRequest = loader.Request(
            "thumb.jpg",
            ThumbnailPriority.Gallery,
            lease => first = lease);
        Assert.IsNotNull(source.Pending);
        source.Pending(CreateImage());

        Assert.IsNotNull(first);
        Assert.IsTrue(cache.Contains("thumb.jpg"));
        Assert.AreEqual(1, source.Requests);

        CachedBitmapLease? second = null;
        using IDisposable secondRequest = loader.Request(
            "thumb.jpg",
            ThumbnailPriority.Foreground,
            lease => second = lease);

        Assert.IsTrue(posted.TryTake(out Action? delivery, TimeSpan.FromSeconds(5)));
        delivery();
        Assert.IsNotNull(second);
        Assert.AreSame(first.Bitmap.Bitmap, second.Bitmap.Bitmap);
        Assert.AreEqual(1, source.Requests);

        first.Dispose();
        second.Dispose();
    }

    [TestMethod]
    public void CancellingACachedRequestDoesNotDeliverTheLease()
    {
        using var posted = new BlockingCollection<Action>();
        var source = new FakeThumbnailSource();
        using var cache = new RenderBitmapCache(1024, _ => { });
        using CachedBitmapLease seeded = cache.AddAndAcquire("thumb.jpg", null!, 1, 1);
        var loader = new ThumbnailImageLoader(
            source,
            cache,
            new WindowSynchronizationContext(posted.Add),
            _ => null!);
        bool delivered = false;

        IDisposable request = loader.Request(
            "thumb.jpg",
            ThumbnailPriority.Gallery,
            _ => delivered = true);
        request.Dispose();

        Assert.IsTrue(posted.TryTake(out Action? delivery, TimeSpan.FromSeconds(5)));
        delivery();
        Assert.IsFalse(delivered);
        Assert.AreEqual(0, source.Requests);
        Assert.AreEqual(1, seeded.Bitmap.PinCount);
    }

    private static DecodedImageUpload CreateImage() => DecodedImageUpload.Allocate(1, 1, 4);

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

    private sealed class NoopSubscription : IDisposable
    {
        internal static readonly NoopSubscription Instance = new();

        public void Dispose()
        {
        }
    }
}
