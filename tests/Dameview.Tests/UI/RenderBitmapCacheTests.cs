using Dameview.UI;
using Vortice.Direct2D1;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class RenderBitmapCacheTests
{
    [TestMethod]
    public void TrimmingProtectsCurrentAndEvictsLeastRecentlyUsedInactiveEntry()
    {
        var disposed = new List<ID2D1Bitmap1>();
        using var cache = new RenderBitmapCache(8, disposed.Add);
        ID2D1Bitmap1 first = null!;
        ID2D1Bitmap1 second = null!;
        ID2D1Bitmap1 third = null!;

        CachedBitmap current = cache.AddAndActivate("first", first, 1, 1);
        cache.AddInactive("second", second, 1, 1);
        cache.AddInactive("third", third, 1, 1);

        Assert.AreSame(current, cache.Current);
        Assert.IsTrue(cache.Contains("first"));
        Assert.IsFalse(cache.Contains("second"));
        Assert.IsTrue(cache.Contains("third"));
        CollectionAssert.AreEqual(new[] { second }, disposed);
    }

    [TestMethod]
    public void ActivatingAnotherEntryMakesTheOldCurrentEvictable()
    {
        var disposed = new List<ID2D1Bitmap1>();
        using var cache = new RenderBitmapCache(8, disposed.Add);
        ID2D1Bitmap1 first = null!;
        ID2D1Bitmap1 second = null!;
        ID2D1Bitmap1 third = null!;

        cache.AddAndActivate("first", first, 1, 1);
        cache.AddInactive("second", second, 1, 1);
        Assert.IsTrue(cache.TryActivate("second", out CachedBitmap? current));
        cache.AddInactive("third", third, 1, 1);

        Assert.AreSame(second, current.Bitmap);
        Assert.IsFalse(cache.Contains("first"));
        Assert.IsTrue(cache.Contains("second"));
        Assert.IsTrue(cache.Contains("third"));
        CollectionAssert.AreEqual(new[] { first }, disposed);
    }
}
