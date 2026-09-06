using Dameview.Imaging;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class ImageRepresentationPolicyTests
{
    [TestMethod]
    public void DimensionBeyondDeviceLimitRequiresTiling()
    {
        var policy = new ImageRepresentationPolicy(8_192, long.MaxValue);

        Assert.IsTrue(policy.RequiresTiling(new ImageInfo(8_193, 1)));
        Assert.IsTrue(policy.RequiresTiling(new ImageInfo(1, 8_193)));
        Assert.IsFalse(policy.RequiresTiling(new ImageInfo(8_192, 1)));
    }

    [TestMethod]
    public void DecodedBytesBeyondBudgetRequireTiling()
    {
        var policy = new ImageRepresentationPolicy(16_384, maximumDecodedBytes: 64);

        Assert.IsFalse(policy.RequiresTiling(new ImageInfo(4, 4)));
        Assert.IsTrue(policy.RequiresTiling(new ImageInfo(4, 5)));
    }

    [TestMethod]
    public void DefaultBudgetTilesPreviouslyObservedLargeDecode()
    {
        var policy = new ImageRepresentationPolicy(16_384);

        Assert.IsTrue(policy.RequiresTiling(new ImageInfo(6_000, 6_000)));
        Assert.IsFalse(policy.RequiresTiling(new ImageInfo(5_946, 3_345)));
    }

    [TestMethod]
    public void LargeDimensionsDoNotOverflowDecodedByteCalculation()
    {
        var policy = new ImageRepresentationPolicy(int.MaxValue, long.MaxValue);

        Assert.IsTrue(policy.RequiresTiling(new ImageInfo(int.MaxValue, int.MaxValue)));
    }
}
