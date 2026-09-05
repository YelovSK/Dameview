using System.Drawing;
using Dameview.Platform;
using Dameview.UI;
using Dameview.UI.Layout;

namespace Dameview.Tests.UI.Layout;

[TestClass]
public sealed class SplitViewTests
{
    [TestMethod]
    public void RightPanelReservesSpaceAndDraggingTheSplitterResizesIt()
    {
        var main = new FixedContent();
        var panel = new FixedContent();
        var splitView = new SplitView(main, panel, UiDesignTokens.Default, initialDividerOffsetDips: 184.0f)
        {
            SecondPaneVisible = true,
        };
        var root = new UiRoot(splitView, UiDpi.Default);
        root.Arrange(new SizeF(1000.0f, 800.0f));

        Assert.AreEqual(new RectangleF(0.0f, 0.0f, 796.0f, 800.0f), splitView.FirstPaneBounds);
        Assert.AreEqual(new RectangleF(804.0f, 12.0f, 184.0f, 776.0f), splitView.SecondPaneBounds);

        root.HandlePointer(new UiPointerEvent(
            UiPointerEventKind.Pressed,
            new PointF(800.0f, 400.0f),
            PointerButton.Primary));
        root.HandlePointer(new UiPointerEvent(
            UiPointerEventKind.Moved,
            new PointF(760.0f, 400.0f)));
        root.HandlePointer(new UiPointerEvent(
            UiPointerEventKind.Released,
            new PointF(760.0f, 400.0f),
            PointerButton.Primary));

        Assert.AreEqual(224.0f, splitView.DividerOffsetDips);
        Assert.AreEqual(764.0f, splitView.SecondPaneBounds.X);
        Assert.AreEqual(756.0f, splitView.FirstPaneBounds.Width);
    }

    [TestMethod]
    public void HiddenPanelLetsTheMainContentUseTheWholeWindow()
    {
        var splitView = new SplitView(
            new FixedContent(),
            new FixedContent(),
            UiDesignTokens.Default,
            initialDividerOffsetDips: 184.0f)
        {
            SecondPaneVisible = false,
        };
        splitView.Measure(new SizeF(1000.0f, 800.0f));
        splitView.Arrange(new RectangleF(0.0f, 0.0f, 1000.0f, 800.0f));

        Assert.AreEqual(new RectangleF(0.0f, 0.0f, 1000.0f, 800.0f), splitView.FirstPaneBounds);
        Assert.AreEqual(RectangleF.Empty, splitView.SecondPaneBounds);
    }

    [TestMethod]
    public void LeftTopAndBottomEdgesArrangeThePanelOnTheRequestedEdge()
    {
        AssertEdge(
            SplitViewEdge.Left,
            new RectangleF(12.0f, 12.0f, 184.0f, 776.0f),
            new RectangleF(204.0f, 0.0f, 796.0f, 800.0f));
        AssertEdge(
            SplitViewEdge.Top,
            new RectangleF(12.0f, 12.0f, 976.0f, 184.0f),
            new RectangleF(0.0f, 204.0f, 1000.0f, 596.0f));
        AssertEdge(
            SplitViewEdge.Bottom,
            new RectangleF(12.0f, 604.0f, 976.0f, 184.0f),
            new RectangleF(0.0f, 0.0f, 1000.0f, 596.0f));

        static void AssertEdge(
            SplitViewEdge edge,
            RectangleF expectedPanel,
            RectangleF expectedMain)
        {
            var splitView = new SplitView(
                new FixedContent(),
                new FixedContent(),
                UiDesignTokens.Default,
                initialDividerOffsetDips: 184.0f,
                edge: edge)
            {
                SecondPaneVisible = true,
            };
            splitView.Measure(new SizeF(1000.0f, 800.0f));
            splitView.Arrange(new RectangleF(0.0f, 0.0f, 1000.0f, 800.0f));

            Assert.AreEqual(expectedPanel, splitView.SecondPaneBounds);
            Assert.AreEqual(expectedMain, splitView.FirstPaneBounds);
        }
    }

    private sealed class FixedContent : UiElement
    {
        protected override SizeF MeasureCore(SizeF availableSize) => availableSize;
    }
}
