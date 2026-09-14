using System.Drawing;
using Dameview.UI.Workspace;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class ViewerLayoutTests
{
    [TestMethod]
    public void StatusPanelCanUseCompactBounds()
    {
        var layout = ViewerLayout.Calculate(
            new SizeF(1000.0f, 800.0f),
            showStatus: true,
            showToolbar: true,
            statusWidthDips: 240.0f,
            statusHeightDips: 32.0f);

        Assert.AreEqual(new RectangleF(0.0f, 0.0f, 1000.0f, 800.0f), layout.Content);
        Assert.AreEqual(new RectangleF(380.0f, 756.0f, 240.0f, 32.0f), layout.Status);
        Assert.AreEqual(new RectangleF(262.0f, 12.0f, 476.0f, 46.0f), layout.Toolbar);
    }

    [TestMethod]
    public void StatusPanelIsEmptyWhenItIsHidden()
    {
        var layout = ViewerLayout.Calculate(
            new SizeF(1000.0f, 800.0f),
            showStatus: false,
            showToolbar: false);

        Assert.AreEqual(new RectangleF(0.0f, 0.0f, 1000.0f, 800.0f), layout.Content);
        Assert.AreEqual(RectangleF.Empty, layout.Status);
        Assert.AreEqual(RectangleF.Empty, layout.Toolbar);
    }

    [TestMethod]
    public void ToolbarCanBeShownWithoutAStatusPanel()
    {
        var layout = ViewerLayout.Calculate(
            new SizeF(1000, 800),
            showStatus: false, showToolbar: true, toolbarWidthDips: 104);

        Assert.AreEqual(RectangleF.Empty, layout.Status);
        Assert.AreEqual(new RectangleF(448, 12, 104, 46), layout.Toolbar);
    }

}
