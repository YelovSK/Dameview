using Dameview.UI.Presentation;
using Vortice.Direct2D1;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class RenderBitmapCacheTests
{
    [TestMethod]
    public void TrimmingProtectsLeasedAndEvictsLeastRecentlyUsedInactiveEntry()
    {
        var disposed = new List<ID2D1Bitmap1>();
        using var cache = new RenderBitmapCache(8, disposed.Add);
        ID2D1Bitmap1 first = null!;
        ID2D1Bitmap1 second = null!;
        ID2D1Bitmap1 third = null!;

        using CachedBitmapLease lease = cache.AddAndAcquire("first", first, 1, 1);
        cache.AddInactive("second", second, 1, 1);
        cache.AddInactive("third", third, 1, 1);

        Assert.AreSame(first, lease.Bitmap.Bitmap);
        Assert.IsTrue(cache.Contains("first"));
        Assert.IsFalse(cache.Contains("second"));
        Assert.IsTrue(cache.Contains("third"));
        CollectionAssert.AreEqual(new[] { second }, disposed);
    }

    [TestMethod]
    public void ReleasingAnEntryMakesItEvictable()
    {
        var disposed = new List<ID2D1Bitmap1>();
        using var cache = new RenderBitmapCache(8, disposed.Add);
        ID2D1Bitmap1 first = null!;
        ID2D1Bitmap1 second = null!;
        ID2D1Bitmap1 third = null!;

        CachedBitmapLease firstLease = cache.AddAndAcquire("first", first, 1, 1);
        cache.AddInactive("second", second, 1, 1);
        Assert.IsTrue(cache.TryAcquire("second", out CachedBitmapLease? secondLease));
        firstLease.Dispose();
        cache.AddInactive("third", third, 1, 1);

        Assert.AreSame(second, secondLease.Bitmap.Bitmap);
        Assert.IsFalse(cache.Contains("first"));
        Assert.IsTrue(cache.Contains("second"));
        Assert.IsTrue(cache.Contains("third"));
        CollectionAssert.AreEqual(new[] { first }, disposed);
        secondLease.Dispose();
    }

    [TestMethod]
    public void MultipleLeasesKeepOneSharedBitmapPinned()
    {
        var disposed = new List<ID2D1Bitmap1>();
        using var cache = new RenderBitmapCache(4, disposed.Add);
        ID2D1Bitmap1 first = null!;
        ID2D1Bitmap1 second = null!;

        CachedBitmapLease firstLease = cache.AddAndAcquire("first", first, 1, 1);
        Assert.IsTrue(cache.TryAcquire("first", out CachedBitmapLease? secondLease));
        firstLease.Dispose();
        cache.AddInactive("second", second, 1, 1);

        Assert.IsTrue(cache.Contains("first"));
        Assert.IsFalse(cache.Contains("second"));
        CollectionAssert.AreEqual(new[] { second }, disposed);

        secondLease.Dispose();
    }
    [TestMethod]
    public void APreloadMayUseFreeSpaceButNeverEvicts()
    {
        // 8 bytes per pixel-pair here: width * height * 4.
        using var cache = new RenderBitmapCache(400, _ => { });
        using CachedBitmapLease displayed = cache.AddAndAcquire("displayed", null!, 10, 5);

        Assert.IsTrue(cache.CanPreload(10, 5), "200 alongside 200 still fits.");
        cache.AddInactive("neighbour", null!, 10, 5);

        // The cache is now full, so a third image must be refused rather than evicting one
        // that would only be fetched again on the next navigation.
        Assert.IsFalse(cache.CanPreload(1, 1));
        Assert.IsFalse(cache.HasPreloadCapacity);
        Assert.IsTrue(cache.Contains("displayed"));
        Assert.IsTrue(cache.Contains("neighbour"));
    }

    [TestMethod]
    public void AnImageTooLargeToShareTheCacheIsNeverPreloaded()
    {
        using var cache = new RenderBitmapCache(400, _ => { });
        using CachedBitmapLease displayed = cache.AddAndAcquire("displayed", null!, 10, 5);

        // Free space alone says yes; the incoming size says no.
        Assert.IsTrue(cache.HasPreloadCapacity);
        Assert.IsFalse(cache.CanPreload(20, 5));
    }

}
