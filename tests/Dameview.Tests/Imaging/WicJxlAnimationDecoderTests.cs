using Dameview.Imaging.Animation;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class WicJxlAnimationDecoderTests
{
    [TestMethod]
    [DataRow(50u, 1000u, 1u, 50.0)]
    [DataRow(3u, 120u, 2u, 50.0)]
    [DataRow(1u, 3u, 1u, 1000.0 / 3)]
    [DataRow(50u, 0u, 1u, 100.0)]
    [DataRow(50u, 1000u, 0u, 100.0)]
    [DataRow(0u, 1000u, 1u, 10.0)]
    [DataRow(uint.MaxValue, 1u, uint.MaxValue, 60000.0)]
    public void ConvertsTicksWithoutIntegerTruncationOrOverflow(
        uint ticks, uint numerator, uint denominator, double milliseconds)
    {
        Assert.AreEqual(milliseconds,
            WicJxlAnimationDecoder.GetFrameDuration(ticks, numerator, denominator).TotalMilliseconds, 0.001);
    }
}
