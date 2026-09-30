using System.Drawing;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;

namespace Dameview.Tests.UI.Layout;

[TestClass]
public sealed class StackPanelTests
{
    [TestMethod]
    public void EqualHorizontalLayoutSharesAvailableWidthBetweenVisibleChildren()
    {
        var first = new FixedElement(SizeF.Empty);
        var hidden = new FixedElement(SizeF.Empty) { IsVisible = false };
        var second = new FixedElement(SizeF.Empty);
        var panel = new StackPanel(UiOrientation.Horizontal, first, hidden, second)
        {
            Spacing = 10.0f,
            Distribution = StackPanelDistribution.Equal,
        };

        panel.Measure(new SizeF(100.0f, 30.0f));
        panel.Arrange(new RectangleF(0.0f, 0.0f, 100.0f, 30.0f));

        Assert.AreEqual(new RectangleF(0.0f, 0.0f, 45.0f, 30.0f), first.Bounds);
        Assert.AreEqual(RectangleF.Empty, hidden.Bounds);
        Assert.AreEqual(new RectangleF(55.0f, 0.0f, 45.0f, 30.0f), second.Bounds);
        Assert.AreSame(second, panel.HitTest(new PointF(60.0f, 10.0f)));
    }

    [TestMethod]
    public void NaturalVerticalLayoutUsesDesiredHeightsAndStretchesWidth()
    {
        var first = new FixedElement(new SizeF(30.0f, 20.0f));
        var second = new FixedElement(new SizeF(40.0f, 25.0f));
        var panel = new StackPanel(UiOrientation.Vertical, first, second)
        {
            Spacing = 5.0f,
        };

        SizeF desired = panel.Measure(new SizeF(100.0f, 100.0f));
        panel.Arrange(new RectangleF(0.0f, 0.0f, 60.0f, 100.0f));

        Assert.AreEqual(new SizeF(40.0f, 50.0f), desired);
        Assert.AreEqual(new RectangleF(0.0f, 0.0f, 60.0f, 20.0f), first.Bounds);
        Assert.AreEqual(new RectangleF(0.0f, 25.0f, 60.0f, 25.0f), second.Bounds);
    }

    [TestMethod]
    public void EqualLayoutShrinksSpacingBeforeProducingNegativeChildSizes()
    {
        var first = new FixedElement(SizeF.Empty);
        var second = new FixedElement(SizeF.Empty);
        var third = new FixedElement(SizeF.Empty);
        var panel = new StackPanel(UiOrientation.Horizontal, first, second, third)
        {
            Spacing = 8.0f,
            Distribution = StackPanelDistribution.Equal,
        };

        panel.Measure(new SizeF(4.0f, 20.0f));
        panel.Arrange(new RectangleF(0.0f, 0.0f, 4.0f, 20.0f));

        Assert.AreEqual(0.0f, first.Bounds.Width);
        Assert.AreEqual(0.0f, second.Bounds.Width);
        Assert.AreEqual(0.0f, third.Bounds.Width);
        Assert.AreEqual(4.0f, third.Bounds.X);
    }

    [TestMethod]
    public void FillChildTakesTheLengthTheOthersLeave()
    {
        var header = new FixedElement(new SizeF(30.0f, 20.0f));
        var fill = new FixedElement(new SizeF(30.0f, 5.0f));
        var footer = new FixedElement(new SizeF(30.0f, 10.0f));
        var panel = new StackPanel(UiOrientation.Vertical, header, fill, footer)
        {
            Spacing = 5.0f,
            Fill = fill,
        };

        panel.Measure(new SizeF(60.0f, 100.0f));
        panel.Arrange(new RectangleF(0.0f, 0.0f, 60.0f, 100.0f));

        Assert.AreEqual(new RectangleF(0.0f, 25.0f, 60.0f, 60.0f), fill.Bounds);
        Assert.AreEqual(new RectangleF(0.0f, 90.0f, 60.0f, 10.0f), footer.Bounds);
    }

    private sealed class FixedElement(SizeF desiredSize) : UiElement
    {
        protected override SizeF MeasureCore(SizeF availableSize) => desiredSize;
    }
}
