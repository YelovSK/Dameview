using Dameview.Imaging;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class DecodedImageCacheTests
{
    [TestMethod]
    public void LeastRecentlyUsedImagesAreEvictedToStayWithinTheByteBudget()
    {
        var cache = new DecodedImageCache(8);
        DecodedImage first = CreateImage(1);
        DecodedImage second = CreateImage(2);
        DecodedImage third = CreateImage(3);

        cache.Add("first", first);
        cache.Add("second", second);
        Assert.IsTrue(cache.TryGet("first", out _));

        cache.Add("third", third);

        Assert.IsTrue(cache.TryGet("first", out DecodedImage? cachedFirst));
        Assert.AreSame(first, cachedFirst);
        Assert.IsFalse(cache.TryGet("second", out _));
        Assert.IsTrue(cache.TryGet("third", out DecodedImage? cachedThird));
        Assert.AreSame(third, cachedThird);
    }

    [TestMethod]
    public void AnImageLargerThanTheBudgetIsNotCached()
    {
        var cache = new DecodedImageCache(3);

        cache.Add("large", CreateImage(1));

        Assert.IsFalse(cache.TryGet("large", out _));
    }

    private static DecodedImage CreateImage(byte value)
    {
        return new DecodedImage(1, 1, 4, [value, value, value, value]);
    }
}
