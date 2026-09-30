using System.Drawing;
using Dameview.UI.Foundation;

namespace Dameview.Tests.UI.Layout;

[TestClass]
public sealed class UiElementLayoutTests
{
    [TestMethod]
    public void DefaultsFillTheWholeSlot()
    {
        var element = new FixedElement(new SizeF(10.0f, 10.0f));

        element.Measure(new SizeF(100.0f, 50.0f));
        element.Arrange(new RectangleF(5.0f, 6.0f, 100.0f, 50.0f));

        Assert.AreEqual(new RectangleF(5.0f, 6.0f, 100.0f, 50.0f), element.Bounds);
    }

    [TestMethod]
    public void MarginIsMeasuredAndKeptClearAroundTheElement()
    {
        var element = new FixedElement(new SizeF(10.0f, 10.0f)) { Margin = new UiThickness(1.0f, 2.0f, 3.0f, 4.0f) };

        SizeF desired = element.Measure(new SizeF(100.0f, 50.0f));
        element.Arrange(new RectangleF(0.0f, 0.0f, 100.0f, 50.0f));

        Assert.AreEqual(new SizeF(14.0f, 16.0f), desired);
        Assert.AreEqual(new RectangleF(1.0f, 2.0f, 96.0f, 44.0f), element.Bounds);
    }

    [TestMethod]
    public void AlignedElementTakesItsDesiredSizeAtThatEdge()
    {
        var centered = new FixedElement(new SizeF(20.0f, 10.0f))
        {
            HorizontalAlignment = UiAlignment.Center,
            VerticalAlignment = UiAlignment.End,
        };

        centered.Measure(new SizeF(100.0f, 50.0f));
        centered.Arrange(new RectangleF(0.0f, 0.0f, 100.0f, 50.0f));

        Assert.AreEqual(new RectangleF(40.0f, 40.0f, 20.0f, 10.0f), centered.Bounds);
    }

    [TestMethod]
    public void MaxWidthLimitsMeasureAndCentersAStretchedElement()
    {
        var element = new FixedElement(new SizeF(float.PositiveInfinity, 10.0f)) { MaxWidth = 40.0f };

        SizeF desired = element.Measure(new SizeF(100.0f, 50.0f));
        element.Arrange(new RectangleF(0.0f, 0.0f, 100.0f, 50.0f));

        Assert.AreEqual(40.0f, element.AvailableWidth);
        Assert.AreEqual(40.0f, desired.Width);
        Assert.AreEqual(new RectangleF(30.0f, 0.0f, 40.0f, 50.0f), element.Bounds);
    }

    [TestMethod]
    public void ChildrenFillTheirParentByDefault()
    {
        var child = new FixedElement(new SizeF(20.0f, 10.0f)) { HorizontalAlignment = UiAlignment.End };
        var parent = new Container(child);

        SizeF desired = parent.Measure(new SizeF(100.0f, 50.0f));
        parent.Arrange(new RectangleF(0.0f, 0.0f, 100.0f, 50.0f));

        Assert.AreEqual(new SizeF(20.0f, 10.0f), desired);
        Assert.AreEqual(new RectangleF(80.0f, 0.0f, 20.0f, 50.0f), child.Bounds);
    }

    private sealed class FixedElement(SizeF desiredSize) : UiElement
    {
        internal float AvailableWidth { get; private set; }

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            AvailableWidth = availableSize.Width;
            return desiredSize;
        }
    }

    private sealed class Container : UiElement
    {
        internal Container(UiElement child) => AddChild(child);
    }
}
