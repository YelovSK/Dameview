using Dameview.Imaging.Loading;
using Vortice.Direct2D1;

namespace Dameview.Tests.Imaging;

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

        using CachedBitmapLease lease = cache.Add("first", 1, 1, default, first);
        AddInactive(cache, "second", second);
        AddInactive(cache, "third", third);

        Assert.AreSame(first, lease.Bitmap);
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

        CachedBitmapLease firstLease = cache.Add("first", 1, 1, default, first);
        AddInactive(cache, "second", second);
        Assert.IsTrue(cache.TryAcquire("second", out CachedBitmapLease? secondLease));
        firstLease.Dispose();
        AddInactive(cache, "third", third);

        Assert.AreSame(second, secondLease.Bitmap);
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

        CachedBitmapLease firstLease = cache.Add("first", 1, 1, default, first);
        Assert.IsTrue(cache.TryAcquire("first", out CachedBitmapLease? secondLease));
        firstLease.Dispose();
        AddInactive(cache, "second", second);

        Assert.IsTrue(cache.Contains("first"));
        Assert.IsFalse(cache.Contains("second"));
        CollectionAssert.AreEqual(new[] { second }, disposed);

        secondLease.Dispose();
    }

    [TestMethod]
    public void ReservationsShareTheFreeSpace()
    {
        // Each 10 x 5 image takes 200 bytes: width * height * 4.
        using var cache = new RenderBitmapCache(400, _ => { });
        using CachedBitmapLease displayed = cache.Add("displayed", 10, 5, default, null!);

        Assert.IsTrue(cache.TryReserve(200));
        Assert.IsFalse(cache.TryReserve(1));
        cache.Unreserve(200);
        Assert.IsTrue(cache.TryReserve(200));
    }

    [TestMethod]
    public void AReservedImageIsAddedWithoutEvictingAnything()
    {
        using var cache = new RenderBitmapCache(400, _ => { });
        cache.Add("older", 10, 5, default, null!).Dispose();

        Assert.IsTrue(cache.TryReserve(200));
        CachedBitmapLease preload = cache.Add("preload", 10, 5, default, null!);
        cache.Unreserve(200);
        preload.Dispose();

        Assert.IsTrue(cache.Contains("older"));
        Assert.IsTrue(cache.Contains("preload"));
    }

    [TestMethod]
    public void TrimmingKeepsReservedSpaceFree()
    {
        using var cache = new RenderBitmapCache(400, _ => { });
        cache.Add("older", 10, 5, default, null!).Dispose();
        Assert.IsTrue(cache.TryReserve(200));

        using CachedBitmapLease displayed = cache.Add("displayed", 10, 5, default, null!);
        cache.Trim();

        Assert.IsFalse(cache.Contains("older"), "The reserved image must still fit when it arrives.");
        Assert.IsTrue(cache.Contains("displayed"));
    }

    private static void AddInactive(RenderBitmapCache cache, string path, ID2D1Bitmap1 bitmap) =>
        cache.Add(path, 1, 1, default, bitmap).Dispose();
}
