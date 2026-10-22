using System.Drawing;
using Dameview.UI.Foundation;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class UiDirtyRegionTests
{
    [TestMethod]
    public void RectanglesClippedToNothingAreDropped()
    {
        var region = new UiDirtyRegion();
        region.Reset(new Size(100, 100));

        region.Add(new Rectangle(100, 10, 20, 20));
        region.Add(new Rectangle(10, 100, 20, 20));
        region.Add(new Rectangle(50, 50, 0, 10));

        Assert.IsTrue(region.IsEmpty);
    }

    [TestMethod]
    public void OverlappingRectanglesMerge()
    {
        var region = new UiDirtyRegion();
        region.Reset(new Size(100, 100));

        region.Add(new Rectangle(0, 0, 20, 20));
        region.Add(new Rectangle(40, 0, 20, 20));
        region.Add(new Rectangle(10, 0, 40, 10));

        CollectionAssert.AreEqual(new[] { new Rectangle(0, 0, 60, 20) }, region.Rectangles.ToArray());
    }

    [TestMethod]
    public void TooManyRectanglesMergeIntoFewer()
    {
        var region = new UiDirtyRegion();
        region.Reset(new Size(1000, 100));

        for (int index = 0; index < 20; index++)
        {
            region.Add(new Rectangle(index * 40, 0, 10, 10));
        }

        Assert.IsLessThanOrEqualTo(UiDirtyRegion.MaxRectangles, region.Rectangles.Length);
        Assert.IsTrue(region.Rectangles.ToArray().Any(rectangle => rectangle.Contains(new Rectangle(760, 0, 10, 10))));
    }
}
