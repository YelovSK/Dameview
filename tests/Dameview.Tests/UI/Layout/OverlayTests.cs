using System.Drawing;
using Dameview.UI;
using Dameview.UI.Layout;

namespace Dameview.Tests.UI.Layout;

[TestClass]
public sealed class OverlayTests
{
    [TestMethod]
    public void MeasuresToLargestVisibleChild()
    {
        var first = new FixedElement(new SizeF(40.0f, 30.0f));
        var second = new FixedElement(new SizeF(50.0f, 20.0f));
        var hidden = new FixedElement(new SizeF(100.0f, 100.0f)) { IsVisible = false };
        var overlay = new Overlay(first, second, hidden);

        SizeF desired = overlay.Measure(new SizeF(200.0f, 150.0f));

        Assert.AreEqual(new SizeF(50.0f, 30.0f), desired);
    }

    [TestMethod]
    public void ArrangesVisibleChildrenToTheSameBounds()
    {
        var first = new FixedElement(SizeF.Empty);
        var second = new FixedElement(SizeF.Empty);
        var hidden = new FixedElement(SizeF.Empty) { IsVisible = false };
        var overlay = new Overlay(first, second, hidden);

        overlay.Arrange(new RectangleF(10.0f, 20.0f, 120.0f, 80.0f));

        var childBounds = new RectangleF(0.0f, 0.0f, 120.0f, 80.0f);
        Assert.AreEqual(childBounds, first.Bounds);
        Assert.AreEqual(childBounds, second.Bounds);
        Assert.AreEqual(RectangleF.Empty, hidden.Bounds);
    }

    [TestMethod]
    public void HitTestingUsesChildZOrderAndIgnoresTheContainerSurface()
    {
        var first = new FixedElement(SizeF.Empty);
        var second = new FixedElement(SizeF.Empty);
        var overlay = new Overlay(first, second);
        overlay.Arrange(new RectangleF(0.0f, 0.0f, 100.0f, 100.0f));

        Assert.AreSame(second, overlay.HitTest(new PointF(50.0f, 50.0f)));

        first.IsVisible = false;
        second.IsVisible = false;
        Assert.IsNull(overlay.HitTest(new PointF(50.0f, 50.0f)));
    }

    private sealed class FixedElement(SizeF desiredSize) : UiElement
    {
        protected override SizeF MeasureCore(SizeF availableSize) => desiredSize;
    }
}
