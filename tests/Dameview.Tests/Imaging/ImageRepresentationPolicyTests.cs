using Dameview.Imaging.Loading;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class ImageRepresentationPolicyTests
{
    [TestMethod]
    public void DimensionBeyondDeviceLimitRequiresTiling()
    {
        var policy = new ImageRepresentationPolicy(8_192);

        Assert.IsTrue(RequiresTiling(policy, new ImageInfo(8_193, 1, 1)));
        Assert.IsTrue(RequiresTiling(policy, new ImageInfo(1, 8_193, 1)));
        Assert.IsFalse(RequiresTiling(policy, new ImageInfo(8_192, 1, 1)));
    }

    [TestMethod]
    public void ImageLargerThanHalfTheCacheRequiresTiling()
    {
        var policy = new ImageRepresentationPolicy(16_384);

        Assert.IsFalse(RequiresTiling(policy, new ImageInfo(4, 4, 1), cacheCapacityBytes: 128));
        Assert.IsTrue(RequiresTiling(policy, new ImageInfo(4, 5, 1), cacheCapacityBytes: 128));
    }

    [TestMethod]
    public void AnimationWinsOverTilingOnlyWhenItCanBePlayed()
    {
        var policy = new ImageRepresentationPolicy(8_192);
        var largeAnimation = new ImageInfo(8_193, 1, 2);

        Assert.AreEqual(ImageRepresentationKind.Animated, policy.Select(largeAnimation, canAnimate: true, long.MaxValue));
        Assert.AreEqual(ImageRepresentationKind.Tiled, policy.Select(largeAnimation, canAnimate: false, long.MaxValue));
        Assert.AreEqual(ImageRepresentationKind.Static, policy.Select(new ImageInfo(1, 1, 1), canAnimate: true, long.MaxValue));
    }

    private static bool RequiresTiling(
        ImageRepresentationPolicy policy,
        ImageInfo image,
        long cacheCapacityBytes = long.MaxValue) =>
        policy.Select(image, canAnimate: false, cacheCapacityBytes) == ImageRepresentationKind.Tiled;
}
