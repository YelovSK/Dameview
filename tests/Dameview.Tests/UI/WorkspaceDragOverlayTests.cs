using System.Drawing;
using Dameview.UI.Components;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class WorkspaceDragOverlayTests
{
    [TestMethod]
    public void DockTargetsSurroundThePaneCenterWithoutOverlapping()
    {
        var pane = new RectangleF(20.0f, 30.0f, 400.0f, 300.0f);

        WorkspaceDockTargets targets = WorkspaceDragOverlay.CalculateDockTargets(pane);

        PointF center = new(pane.Left + pane.Width / 2.0f, pane.Top + pane.Height / 2.0f);
        Assert.IsLessThan(center.X, targets.Left.Right);
        Assert.IsGreaterThan(center.X, targets.Right.Left);
        Assert.IsLessThan(center.Y, targets.Up.Bottom);
        Assert.IsGreaterThan(center.Y, targets.Down.Top);
        Assert.IsFalse(targets.Left.IntersectsWith(targets.Up));
        Assert.IsFalse(targets.Left.IntersectsWith(targets.Down));
        Assert.IsFalse(targets.Right.IntersectsWith(targets.Up));
        Assert.IsFalse(targets.Right.IntersectsWith(targets.Down));
    }
}
