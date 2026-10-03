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

        using CachedBitmapLease lease = cache.GetOrAdd("first", 1, 1, default, () => first);
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

        CachedBitmapLease firstLease = cache.GetOrAdd("first", 1, 1, default, () => first);
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

        CachedBitmapLease firstLease = cache.GetOrAdd("first", 1, 1, default, () => first);
        Assert.IsTrue(cache.TryAcquire("first", out CachedBitmapLease? secondLease));
        firstLease.Dispose();
        AddInactive(cache, "second", second);

        Assert.IsTrue(cache.Contains("first"));
        Assert.IsFalse(cache.Contains("second"));
        CollectionAssert.AreEqual(new[] { second }, disposed);

        secondLease.Dispose();
    }

    [TestMethod]
    public void AddingACachedPathReusesItsBitmap()
    {
        using var cache = new RenderBitmapCache(8, _ => { });
        using CachedBitmapLease first = cache.GetOrAdd("image", 1, 1, default, () => null!);

        using CachedBitmapLease second = cache.GetOrAdd("image", 1, 1, default, NotCreated);

        Assert.AreSame(first.Entry, second.Entry);
        Assert.AreEqual(2, first.Entry.PinCount);
    }

    [TestMethod]
    public void APreloadMayUseFreeSpaceButNeverEvicts()
    {
        // Each 10 x 5 image takes 200 bytes: width * height * 4.
        using var cache = new RenderBitmapCache(400, _ => { });
        using CachedBitmapLease displayed = cache.GetOrAdd("displayed", 10, 5, default, () => null!);

        cache.TryPreload("neighbour", 10, 5, default, () => null!);
        Assert.IsTrue(cache.Contains("neighbour"), "200 alongside 200 still fits.");

        // The cache is now full, so a third image must be refused rather than evicting one
        // that would only be fetched again on the next navigation.
        cache.TryPreload("third", 1, 1, default, NotCreated);
        Assert.IsFalse(cache.Contains("third"));
        Assert.AreEqual(0, cache.FreeBytes);
        Assert.IsTrue(cache.Contains("displayed"));
        Assert.IsTrue(cache.Contains("neighbour"));
    }

    [TestMethod]
    public void AnImageTooLargeToShareTheCacheIsNeverPreloaded()
    {
        using var cache = new RenderBitmapCache(400, _ => { });
        using CachedBitmapLease displayed = cache.GetOrAdd("displayed", 10, 5, default, () => null!);

        // Free space alone says yes; the incoming size says no.
        Assert.AreEqual(200, cache.FreeBytes);
        cache.TryPreload("large", 20, 5, default, NotCreated);
        Assert.IsFalse(cache.Contains("large"));
    }

    private static void AddInactive(RenderBitmapCache cache, string path, ID2D1Bitmap1 bitmap) =>
        cache.GetOrAdd(path, 1, 1, default, () => bitmap).Dispose();

    private static ID2D1Bitmap1 NotCreated() => throw new AssertFailedException("The bitmap was created.");
}
