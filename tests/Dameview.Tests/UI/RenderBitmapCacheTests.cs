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
}
