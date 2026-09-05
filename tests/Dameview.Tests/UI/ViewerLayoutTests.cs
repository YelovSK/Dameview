using System.Drawing;
using Dameview.UI;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class ViewerLayoutTests
{
    [TestMethod]
    public void StatusPanelOverlaysTheFullSizeContentAtTheCurrentDpi()
    {
        ViewerLayout layout = ViewerLayout.Calculate(
            new SizeF(1000.0f, 800.0f),
            144.0f,
            showStatus: true,
            showToolbar: true);

        Assert.AreEqual(new RectangleF(0.0f, 0.0f, 1000.0f, 800.0f), layout.Content);
        Assert.AreEqual(new RectangleF(18.0f, 719.0f, 964.0f, 63.0f), layout.Status);
        Assert.AreEqual(new RectangleF(251.0f, 638.0f, 498.0f, 69.0f), layout.Toolbar);
    }

    [TestMethod]
    public void StatusPanelIsEmptyWhenItIsHidden()
    {
        ViewerLayout layout = ViewerLayout.Calculate(
            new SizeF(1000.0f, 800.0f),
            UiDpi.Default,
            showStatus: false,
            showToolbar: false);

        Assert.AreEqual(new RectangleF(0.0f, 0.0f, 1000.0f, 800.0f), layout.Content);
        Assert.AreEqual(RectangleF.Empty, layout.Status);
        Assert.AreEqual(RectangleF.Empty, layout.Toolbar);
    }

    [TestMethod]
    public void SettingsToolbarIsAvailableWithoutAStatusPanel()
    {
        ViewerLayout layout = ViewerLayout.Calculate(
            new SizeF(1000, 800), 144,
            showStatus: false, showToolbar: true, toolbarWidthDips: 104);

        Assert.AreEqual(RectangleF.Empty, layout.Status);
        Assert.AreEqual(new RectangleF(422, 713, 156, 69), layout.Toolbar);
    }
}
