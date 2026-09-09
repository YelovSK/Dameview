using System.Drawing;
using Dameview.UI;
using Dameview.UI.Layout;

namespace Dameview.Tests.UI.Layout;

[TestClass]
public sealed class SplitPanelTests
{
    [TestMethod]
    public void HorizontalLayoutUsesRatioAcrossAvailableWidth()
    {
        var first = new FixedContent();
        var second = new FixedContent();
        var panel = new SplitPanel(first, second, UiOrientation.Horizontal, 0.6f);

        panel.Measure(new SizeF(1008.0f, 600.0f));
        panel.Arrange(new RectangleF(0.0f, 0.0f, 1008.0f, 600.0f));

        Assert.AreEqual(new RectangleF(0.0f, 0.0f, 600.0f, 600.0f), first.Bounds);
        Assert.AreEqual(new RectangleF(608.0f, 0.0f, 400.0f, 600.0f), second.Bounds);
    }

    [TestMethod]
    public void VerticalLayoutRespectsTheMinimumPaneSize()
    {
        var first = new FixedContent();
        var second = new FixedContent();
        var panel = new SplitPanel(first, second, UiOrientation.Vertical, 0.25f);

        panel.Measure(new SizeF(500.0f, 408.0f));
        panel.Arrange(new RectangleF(0.0f, 0.0f, 500.0f, 408.0f));

        Assert.AreEqual(new RectangleF(0.0f, 0.0f, 500.0f, 120.0f), first.Bounds);
        Assert.AreEqual(new RectangleF(0.0f, 128.0f, 500.0f, 280.0f), second.Bounds);
    }

    [TestMethod]
    public void LayoutKeepsBothPanesAboveTheMinimumWhenSpaceAllows()
    {
        var first = new FixedContent();
        var second = new FixedContent();
        var panel = new SplitPanel(first, second, UiOrientation.Horizontal, 0.9f);

        panel.Measure(new SizeF(308.0f, 200.0f));
        panel.Arrange(new RectangleF(0.0f, 0.0f, 308.0f, 200.0f));

        Assert.AreEqual(180.0f, first.Bounds.Width);
        Assert.AreEqual(120.0f, second.Bounds.Width);
    }

    [TestMethod]
    public void CrampedLayoutDoesNotProduceNegativeBounds()
    {
        var first = new FixedContent();
        var second = new FixedContent();
        var panel = new SplitPanel(first, second, UiOrientation.Horizontal, 0.5f);

        panel.Measure(new SizeF(4.0f, 20.0f));
        panel.Arrange(new RectangleF(0.0f, 0.0f, 4.0f, 20.0f));

        Assert.AreEqual(0.0f, first.Bounds.Width);
        Assert.AreEqual(0.0f, second.Bounds.Width);
        Assert.AreEqual(4.0f, second.Bounds.X);
    }

    private sealed class FixedContent : UiElement
    {
        protected override SizeF MeasureCore(SizeF availableSize) => availableSize;
    }
}
