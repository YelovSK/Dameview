using System.Drawing;
using Dameview.UI.Workspace;
using Dameview.Viewing;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class WorkspaceDragControllerTests
{
    private static readonly RectangleF Pane = new(20.0f, 30.0f, 400.0f, 300.0f);

    [TestMethod]
    public void EachEdgeSplitsTowardItself()
    {
        Assert.AreEqual(WorkspacePaneDropSide.Left, GetSplitSide(0.1f, 0.5f));
        Assert.AreEqual(WorkspacePaneDropSide.Right, GetSplitSide(0.9f, 0.5f));
        Assert.AreEqual(WorkspacePaneDropSide.Top, GetSplitSide(0.5f, 0.1f));
        Assert.AreEqual(WorkspacePaneDropSide.Bottom, GetSplitSide(0.5f, 0.9f));
    }

    [TestMethod]
    public void CornersSplitTowardTheNearerEdgeRelativeToThePaneSize()
    {
        Assert.AreEqual(WorkspacePaneDropSide.Left, GetSplitSide(0.05f, 0.2f));
        Assert.AreEqual(WorkspacePaneDropSide.Top, GetSplitSide(0.2f, 0.05f));
    }

    [TestMethod]
    public void TheMiddleOfThePaneDoesNotSplit()
    {
        Assert.IsNull(GetSplitSide(0.5f, 0.5f));
        Assert.IsNull(GetSplitSide(0.45f, 0.55f));
    }

    [TestMethod]
    public void TheLandingAreaIsTheHalfOnTheSplitSide()
    {
        Assert.AreEqual(
            new RectangleF(220.0f, 30.0f, 200.0f, 300.0f),
            WorkspaceDragController.GetLandingBounds(Pane, WorkspacePaneDropSide.Right));
        Assert.AreEqual(
            new RectangleF(20.0f, 30.0f, 400.0f, 150.0f),
            WorkspaceDragController.GetLandingBounds(Pane, WorkspacePaneDropSide.Top));
    }

    private static WorkspacePaneDropSide? GetSplitSide(float x, float y) =>
        WorkspaceDragController.GetSplitSide(
            Pane,
            new PointF(Pane.Left + x * Pane.Width, Pane.Top + y * Pane.Height));
}
