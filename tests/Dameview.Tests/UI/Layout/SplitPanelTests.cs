using System.Drawing;
using Dameview.Platform;
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

    [TestMethod]
    public void DraggingTheDividerUpdatesTheRatioAndBothPaneBounds()
    {
        var first = new FixedContent();
        var second = new FixedContent();
        float changedRatio = 0.0f;
        var panel = new SplitPanel(
            first,
            second,
            UiOrientation.Horizontal,
            0.6f,
            ratio => changedRatio = ratio);
        var root = new UiRoot(panel, UiDpi.Default);
        root.Arrange(new SizeF(1008.0f, 600.0f));

        root.HandlePointer(Pointer(UiPointerEventKind.Pressed, 604.0f, 300.0f));
        root.HandlePointer(Pointer(UiPointerEventKind.Moved, 704.0f, 300.0f));
        root.HandlePointer(Pointer(UiPointerEventKind.Released, 704.0f, 300.0f));

        Assert.AreEqual(0.7f, changedRatio, 0.0001f);
        Assert.AreEqual(700.0f, first.Bounds.Width);
        Assert.AreEqual(300.0f, second.Bounds.Width);
    }

    [TestMethod]
    public void DividerDraggingCannotShrinkEitherPaneBelowTheMinimum()
    {
        var first = new FixedContent();
        var second = new FixedContent();
        var panel = new SplitPanel(first, second, UiOrientation.Vertical, 0.5f);
        var root = new UiRoot(panel, UiDpi.Default);
        root.Arrange(new SizeF(400.0f, 408.0f));

        root.HandlePointer(Pointer(UiPointerEventKind.Pressed, 200.0f, 204.0f));
        root.HandlePointer(Pointer(UiPointerEventKind.Moved, 200.0f, 500.0f));
        root.HandlePointer(Pointer(UiPointerEventKind.Released, 200.0f, 500.0f));

        Assert.AreEqual(280.0f, first.Bounds.Height);
        Assert.AreEqual(SplitPanel.MinimumPaneSizeDips, second.Bounds.Height);
    }

    [TestMethod]
    public void RatioChangesAnimatePaneGeometry()
    {
        var first = new FixedContent();
        var second = new FixedContent();
        var panel = new SplitPanel(first, second, UiOrientation.Horizontal, 0.5f);
        var root = new UiRoot(panel, UiDpi.Default);
        root.Arrange(new SizeF(1008.0f, 600.0f));

        panel.SetRatio(0.75f);
        Assert.IsTrue(root.Update(new UiUpdateContext(0.05)));
        root.Arrange(new SizeF(1008.0f, 600.0f));

        Assert.IsGreaterThan(500.0f, first.Bounds.Width);
        Assert.IsLessThan(750.0f, first.Bounds.Width);

        CompleteAnimations(root);
        root.Arrange(new SizeF(1008.0f, 600.0f));
        Assert.AreEqual(750.0f, first.Bounds.Width);
        Assert.AreEqual(250.0f, second.Bounds.Width);
    }

    [TestMethod]
    public void DisabledAnimationsApplyRatioChangesImmediately()
    {
        var first = new FixedContent();
        var second = new FixedContent();
        var panel = new SplitPanel(first, second, UiOrientation.Vertical, 0.5f);
        var root = new UiRoot(panel, UiDpi.Default);
        root.Arrange(new SizeF(600.0f, 1008.0f));

        panel.SetRatio(0.75f);
        Assert.IsFalse(root.Update(new UiUpdateContext(0.0, AnimationsEnabled: false)));
        root.Arrange(new SizeF(600.0f, 1008.0f));

        Assert.AreEqual(750.0f, first.Bounds.Height);
        Assert.AreEqual(250.0f, second.Bounds.Height);
    }

    [TestMethod]
    public void OpeningAnimationExpandsTheSecondPaneFromTheOuterEdge()
    {
        var first = new FixedContent();
        var second = new FixedContent();
        var panel = new SplitPanel(
            first,
            second,
            UiOrientation.Horizontal,
            0.6f,
            animateOpening: true);
        var root = new UiRoot(panel, UiDpi.Default);

        root.Arrange(new SizeF(1008.0f, 600.0f));
        Assert.AreEqual(1008.0f, first.Bounds.Width);
        Assert.AreEqual(1008.0f, second.Bounds.X);
        Assert.AreEqual(0.0f, second.Bounds.Width);

        Assert.IsTrue(root.Update(new UiUpdateContext(0.05)));
        root.Arrange(new SizeF(1008.0f, 600.0f));
        Assert.IsGreaterThan(600.0f, first.Bounds.Width);
        Assert.IsLessThan(1008.0f, first.Bounds.Width);
        Assert.IsGreaterThan(0.0f, second.Bounds.Width);
        Assert.IsLessThan(400.0f, second.Bounds.Width);

        for (int frame = 0; frame < 10; frame++)
        {
            root.Update(new UiUpdateContext(0.05));
        }

        root.Arrange(new SizeF(1008.0f, 600.0f));
        Assert.AreEqual(600.0f, first.Bounds.Width);
        Assert.AreEqual(608.0f, second.Bounds.X);
        Assert.AreEqual(400.0f, second.Bounds.Width);
    }

    [TestMethod]
    public void CollapsingTheSecondPaneExpandsTheFirstPaneToTheOuterEdge()
    {
        var first = new FixedContent();
        var second = new FixedContent();
        var panel = new SplitPanel(first, second, UiOrientation.Horizontal, 0.6f);
        var root = new UiRoot(panel, UiDpi.Default);
        int completions = 0;
        root.Arrange(new SizeF(1008.0f, 600.0f));

        panel.Collapse(second, () => completions++);
        Assert.IsTrue(root.Update(new UiUpdateContext(0.05)));
        root.Arrange(new SizeF(1008.0f, 600.0f));
        Assert.IsGreaterThan(600.0f, first.Bounds.Width);
        Assert.IsLessThan(1008.0f, first.Bounds.Width);
        Assert.IsGreaterThan(0.0f, second.Bounds.Width);
        Assert.IsLessThan(400.0f, second.Bounds.Width);

        CompleteAnimations(root);
        root.Arrange(new SizeF(1008.0f, 600.0f));
        Assert.AreEqual(1, completions);
        Assert.AreEqual(1008.0f, first.Bounds.Width);
        Assert.AreEqual(1008.0f, second.Bounds.X);
        Assert.AreEqual(0.0f, second.Bounds.Width);
    }

    [TestMethod]
    public void DisabledAnimationsCollapseImmediatelyAndRunCompletion()
    {
        var first = new FixedContent();
        var second = new FixedContent();
        var panel = new SplitPanel(first, second, UiOrientation.Horizontal, 0.6f);
        var root = new UiRoot(panel, UiDpi.Default);
        int completions = 0;
        root.Arrange(new SizeF(1008.0f, 600.0f));

        panel.Collapse(second, () => completions++);
        Assert.IsFalse(root.Update(new UiUpdateContext(0.0, AnimationsEnabled: false)));
        root.Arrange(new SizeF(1008.0f, 600.0f));

        Assert.AreEqual(1, completions);
        Assert.AreEqual(1008.0f, first.Bounds.Width);
        Assert.AreEqual(0.0f, second.Bounds.Width);
    }

    [TestMethod]
    public void CollapsingTheFirstPaneExpandsTheSecondPaneToTheOuterEdge()
    {
        var first = new FixedContent();
        var second = new FixedContent();
        var panel = new SplitPanel(first, second, UiOrientation.Vertical, 0.25f);
        var root = new UiRoot(panel, UiDpi.Default);
        int completions = 0;
        root.Arrange(new SizeF(500.0f, 408.0f));

        panel.Collapse(first, () => completions++);
        CompleteAnimations(root);
        root.Arrange(new SizeF(500.0f, 408.0f));

        Assert.AreEqual(1, completions);
        Assert.AreEqual(0.0f, first.Bounds.Height);
        Assert.AreEqual(0.0f, second.Bounds.Y);
        Assert.AreEqual(408.0f, second.Bounds.Height);
    }

    [TestMethod]
    public void CollapsingTheFirstPaneDuringOpeningDoesNotJumpToTheFinalLayout()
    {
        var first = new FixedContent();
        var second = new FixedContent();
        var panel = new SplitPanel(
            first,
            second,
            UiOrientation.Horizontal,
            0.5f,
            animateOpening: true);
        var root = new UiRoot(panel, UiDpi.Default);
        int completions = 0;
        root.Arrange(new SizeF(1008.0f, 600.0f));

        panel.Collapse(first, () => completions++);
        root.Arrange(new SizeF(1008.0f, 600.0f));
        Assert.AreEqual(1008.0f, first.Bounds.Width);
        Assert.AreEqual(0.0f, second.Bounds.Width);

        CompleteAnimations(root, frameCount: 20);
        root.Arrange(new SizeF(1008.0f, 600.0f));
        Assert.AreEqual(1, completions);
        Assert.AreEqual(0.0f, first.Bounds.Width);
        Assert.AreEqual(1008.0f, second.Bounds.Width);
    }

    private static void CompleteAnimations(UiRoot root, int frameCount = 10)
    {
        for (int frame = 0; frame < frameCount; frame++)
        {
            root.Update(new UiUpdateContext(0.05));
        }
    }

    private static UiPointerEvent Pointer(UiPointerEventKind kind, float x, float y)
    {
        return new UiPointerEvent(kind, new PointF(x, y), PointerButton.Primary);
    }

    private sealed class FixedContent : UiElement
    {
        protected override SizeF MeasureCore(SizeF availableSize) => availableSize;
    }
}
