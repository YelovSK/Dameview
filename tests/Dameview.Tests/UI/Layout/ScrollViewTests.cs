using System.Drawing;
using Dameview.Platform;
using Dameview.UI;
using Dameview.UI.Layout;

namespace Dameview.Tests.UI.Layout;

[TestClass]
public sealed class ScrollViewTests
{
    [TestMethod]
    public void WheelScrollingUsesContentHeightAndClampsAtBothEnds()
    {
        var content = new FixedContent(new SizeF(200.0f, 500.0f));
        var scrollView = new ScrollView(content);
        scrollView.Measure(new SizeF(200.0f, 120.0f));
        scrollView.Arrange(new RectangleF(0.0f, 0.0f, 200.0f, 120.0f));

        scrollView.OnPointerEvent(new UiPointerEvent(
            UiPointerEventKind.Wheel,
            PointF.Empty,
            WheelDelta: -1200));

        AdvanceScroll(scrollView);
        Assert.AreEqual(380.0f, scrollView.ScrollOffset);
        Assert.AreEqual(-380.0f, content.Bounds.Y);

        scrollView.OnPointerEvent(new UiPointerEvent(
            UiPointerEventKind.Wheel,
            PointF.Empty,
            WheelDelta: 1200));
        AdvanceScroll(scrollView);
        Assert.AreEqual(0.0f, scrollView.ScrollOffset);
    }

    private static void AdvanceScroll(ScrollView scrollView)
    {
        for (int frame = 0; frame < 120; frame++)
        {
            scrollView.UpdateTree(new UiUpdateContext(1.0 / 60.0));
        }
    }

    [TestMethod]
    public void BringingFocusedContentIntoViewScrollsOnlyAsFarAsNeeded()
    {
        var focus = new FocusTarget();
        var content = new ContentWithFocus(focus);
        var scrollView = new ScrollView(content);
        var root = new UiRoot(scrollView, UiDpi.Default);
        root.Arrange(new SizeF(200.0f, 120.0f));

        root.SetFocus(focus);

        Assert.AreEqual(310.0f, scrollView.ScrollOffset);
        RectangleF focusedBounds = focus.GetBoundsRelativeTo(scrollView);
        Assert.AreEqual(120.0f, focusedBounds.Bottom);
    }

    [TestMethod]
    public void OverflowShowsAScrollbarAndDraggingItsThumbScrollsContent()
    {
        var content = new FixedContent(new SizeF(200.0f, 500.0f));
        var scrollView = new ScrollView(content);
        var root = new UiRoot(scrollView, UiDpi.Default);
        root.Arrange(new SizeF(200.0f, 120.0f));

        Assert.IsTrue(scrollView.IsScrollbarVisible);
        RectangleF scrollbar = scrollView.Children[^1].Bounds;
        RectangleF thumb = scrollView.ScrollbarThumbBounds;
        PointF press = new(scrollbar.X + thumb.X + 4.0f, scrollbar.Y + thumb.Y + thumb.Height / 2.0f);
        root.HandlePointer(new UiPointerEvent(UiPointerEventKind.Pressed, press, PointerButton.Primary));
        root.HandlePointer(new UiPointerEvent(
            UiPointerEventKind.Moved,
            new PointF(press.X, scrollbar.Bottom - 4.0f)));
        root.HandlePointer(new UiPointerEvent(
            UiPointerEventKind.Released,
            new PointF(press.X, scrollbar.Bottom - 4.0f),
            PointerButton.Primary));

        Assert.IsTrue(scrollView.ScrollOffset > 0.0f);
        Assert.AreEqual(-scrollView.ScrollOffset, content.Bounds.Y);
    }

    [TestMethod]
    public void ContentThatFitsDoesNotShowAScrollbar()
    {
        var scrollView = new ScrollView(new FixedContent(new SizeF(200.0f, 100.0f)));
        scrollView.Measure(new SizeF(200.0f, 120.0f));
        scrollView.Arrange(new RectangleF(0.0f, 0.0f, 200.0f, 120.0f));

        Assert.IsFalse(scrollView.IsScrollbarVisible);
    }

    private sealed class FixedContent(SizeF desiredSize) : UiElement
    {
        protected override SizeF MeasureCore(SizeF availableSize) => desiredSize;
    }

    private sealed class ContentWithFocus : UiElement
    {
        private readonly UiElement _focus;

        internal ContentWithFocus(UiElement focus)
        {
            _focus = focus;
            AddChild(focus);
        }

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            _focus.Measure(new SizeF(100.0f, 30.0f));
            return new SizeF(availableSize.Width, 500.0f);
        }

        protected override void ArrangeCore(SizeF finalSize)
        {
            _focus.Arrange(new RectangleF(20.0f, 400.0f, 100.0f, 30.0f));
        }
    }

    private sealed class FocusTarget : UiElement
    {
        internal override bool IsFocusable => true;
    }
}
