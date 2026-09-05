using System.Drawing;
using Dameview.UI;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class ViewerLayoutTests
{
    [TestMethod]
    public void StatusPanelOverlaysTheFullSizeContentInDips()
    {
        ViewerLayout layout = ViewerLayout.Calculate(
            new SizeF(1000.0f, 800.0f),
            UiDesignTokens.Default,
            showStatus: true,
            showToolbar: true);

        Assert.AreEqual(new RectangleF(0.0f, 0.0f, 1000.0f, 800.0f), layout.Content);
        Assert.AreEqual(new RectangleF(12.0f, 746.0f, 976.0f, 42.0f), layout.Status);
        Assert.AreEqual(new RectangleF(334.0f, 692.0f, 332.0f, 46.0f), layout.Toolbar);
    }

    [TestMethod]
    public void StatusPanelIsEmptyWhenItIsHidden()
    {
        ViewerLayout layout = ViewerLayout.Calculate(
            new SizeF(1000.0f, 800.0f),
            UiDesignTokens.Default,
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
            new SizeF(1000, 800), UiDesignTokens.Default,
            showStatus: false, showToolbar: true, toolbarWidthDips: 104);

        Assert.AreEqual(RectangleF.Empty, layout.Status);
        Assert.AreEqual(new RectangleF(448, 742, 104, 46), layout.Toolbar);
    }
}
