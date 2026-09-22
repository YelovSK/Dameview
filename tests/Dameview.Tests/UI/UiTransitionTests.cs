using System.Drawing;
using Dameview.UI.Animation;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class UiTransitionTests
{
    private static readonly SizeF WindowSize = new(100.0f, 200.0f);

    [TestMethod]
    public void ALeavingElementStopsTakingInputButStaysVisibleUntilItsExitHasFinished()
    {
        var element = new Box { Transition = new UiTransition(Fade: true) };
        var root = new UiRoot(
            new StackPanel(UiOrientation.Vertical, 0.0f, StackPanelDistribution.Natural, element),
            UiDpi.Default,
            TestTextLayouts.Shared);
        root.Arrange(WindowSize);

        element.IsPresent = false;

        Assert.IsTrue(element.IsVisible);
        Assert.IsNull(element.HitTest(new PointF(10.0f, 10.0f)));
        root.Update(new UiUpdateContext(0.01));
        Assert.IsTrue(element.Opacity is > 0.0f and < 1.0f);

        root.Update(new UiUpdateContext(0.0, AnimationsEnabled: false));

        Assert.IsFalse(element.IsVisible);
    }

    [TestMethod]
    public void AnElementThatIsNotOnScreenChangesPresenceAtOnce()
    {
        var element = new Box { Transition = new UiTransition(Fade: true) };

        element.IsPresent = false;

        Assert.IsFalse(element.IsVisible);
        Assert.AreEqual(0.0f, element.Presence);
    }

    [TestMethod]
    public void ACollapsingChildGivesUpItsSlotAndGapInAStack()
    {
        var first = new Box();
        var collapsing = new Box { Transition = new UiTransition(Collapse: true) };
        var last = new Box();
        var root = new UiRoot(
            new StackPanel(UiOrientation.Vertical, 10.0f, StackPanelDistribution.Natural, first, collapsing, last),
            UiDpi.Default,
            TestTextLayouts.Shared);
        root.Arrange(WindowSize);
        Assert.AreEqual(60.0f, last.Bounds.Top);

        collapsing.IsPresent = false;
        root.Update(new UiUpdateContext(0.01));
        root.Arrange(WindowSize);

        float presence = collapsing.Presence;
        Assert.IsTrue(presence is > 0.0f and < 1.0f);
        Assert.AreEqual(30.0f + (30.0f * presence), last.Bounds.Top, 0.001f);

        root.Update(new UiUpdateContext(0.0, AnimationsEnabled: false));
        root.Arrange(WindowSize);

        Assert.AreEqual(30.0f, last.Bounds.Top);
    }

    [TestMethod]
    public void AStackEndsExactlyAtItsLastChildWhileThatChildCollapses()
    {
        var first = new Box();
        var collapsing = new Box { Transition = new UiTransition(Collapse: true) };
        var stack = new StackPanel(UiOrientation.Vertical, 10.0f, StackPanelDistribution.Natural, first, collapsing);
        var root = new UiRoot(stack, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(WindowSize);

        collapsing.IsPresent = false;
        root.Update(new UiUpdateContext(0.01));
        root.Arrange(WindowSize);

        Assert.IsTrue(collapsing.Presence is > 0.0f and < 1.0f);
        Assert.AreEqual(collapsing.Bounds.Bottom, stack.DesiredSize.Height, 0.001f);
    }

    private sealed class Box : UiElement
    {
        protected override SizeF MeasureCore(SizeF availableSize) => new(availableSize.Width, 20.0f);
    }
}
