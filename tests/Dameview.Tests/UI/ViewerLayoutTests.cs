using System.Drawing;
using Dameview.UI.Workspace;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class ViewerLayoutTests
{
    [TestMethod]
    public void StatusPanelOverlaysTheFullSizeContentInDips()
    {
        var layout = ViewerLayout.Calculate(
            new SizeF(1000.0f, 800.0f),
            showStatus: true,
            showToolbar: true);

        Assert.AreEqual(new RectangleF(0.0f, 0.0f, 1000.0f, 800.0f), layout.Content);
        Assert.AreEqual(new RectangleF(12.0f, 746.0f, 976.0f, 42.0f), layout.Status);
        Assert.AreEqual(new RectangleF(262.0f, 12.0f, 476.0f, 46.0f), layout.Toolbar);
        Assert.AreEqual(RectangleF.Empty, layout.Gallery);
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

    [TestMethod]
    public void GalleryReservesTheRightSideForTheImageViewportAndOverlays()
    {
        var layout = ViewerLayout.Calculate(
            new SizeF(1000.0f, 800.0f),
            showStatus: true,
            showToolbar: true,
            showGallery: true,
            galleryWidthDips: 184.0f);

        Assert.AreEqual(new RectangleF(0.0f, 0.0f, 796.0f, 800.0f), layout.Content);
        Assert.AreEqual(new RectangleF(12.0f, 746.0f, 772.0f, 42.0f), layout.Status);
        Assert.AreEqual(new RectangleF(160.0f, 12.0f, 476.0f, 46.0f), layout.Toolbar);
        Assert.AreEqual(new RectangleF(804.0f, 12.0f, 184.0f, 776.0f), layout.Gallery);
    }
}
