using Dameview.Imaging.Loading;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class ImageRepresentationPolicyTests
{
    [TestMethod]
    public void DimensionBeyondDeviceLimitRequiresTiling()
    {
        var policy = new ImageRepresentationPolicy(8_192, long.MaxValue);

        Assert.IsTrue(RequiresTiling(policy, new ImageInfo(8_193, 1, 1)));
        Assert.IsTrue(RequiresTiling(policy, new ImageInfo(1, 8_193, 1)));
        Assert.IsFalse(RequiresTiling(policy, new ImageInfo(8_192, 1, 1)));
    }

    [TestMethod]
    public void DecodedBytesBeyondBudgetRequireTiling()
    {
        var policy = new ImageRepresentationPolicy(16_384, maximumDecodedBytes: 64);

        Assert.IsFalse(RequiresTiling(policy, new ImageInfo(4, 4, 1)));
        Assert.IsTrue(RequiresTiling(policy, new ImageInfo(4, 5, 1)));
    }

    [TestMethod]
    public void DefaultBudgetTilesPreviouslyObservedLargeDecode()
    {
        var policy = new ImageRepresentationPolicy(16_384);

        Assert.IsTrue(RequiresTiling(policy, new ImageInfo(6_000, 6_000, 1)));
        Assert.IsFalse(RequiresTiling(policy, new ImageInfo(5_946, 3_345, 1)));
    }

    [TestMethod]
    public void LargeDimensionsDoNotOverflowDecodedByteCalculation()
    {
        var policy = new ImageRepresentationPolicy(int.MaxValue, long.MaxValue);

        Assert.IsTrue(RequiresTiling(policy, new ImageInfo(int.MaxValue, int.MaxValue, 1)));
    }

    [TestMethod]
    public void AnimationWinsOverTilingOnlyWhenItCanBePlayed()
    {
        var policy = new ImageRepresentationPolicy(8_192, long.MaxValue);
        var largeAnimation = new ImageInfo(8_193, 1, 2);

        Assert.AreEqual(ImageRepresentationKind.Animated, policy.Select(largeAnimation, canAnimate: true));
        Assert.AreEqual(ImageRepresentationKind.Tiled, policy.Select(largeAnimation, canAnimate: false));
        Assert.AreEqual(ImageRepresentationKind.Static, policy.Select(new ImageInfo(1, 1, 1), canAnimate: true));
    }

    private static bool RequiresTiling(ImageRepresentationPolicy policy, ImageInfo image) =>
        policy.Select(image, canAnimate: false) == ImageRepresentationKind.Tiled;
}
