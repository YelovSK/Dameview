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
            showStatus: true);

        Assert.AreEqual(new RectangleF(0.0f, 0.0f, 1000.0f, 800.0f), layout.Content);
        Assert.AreEqual(new RectangleF(18.0f, 719.0f, 964.0f, 63.0f), layout.Status);
    }

    [TestMethod]
    public void StatusPanelIsEmptyWhenItIsHidden()
    {
        ViewerLayout layout = ViewerLayout.Calculate(
            new SizeF(1000.0f, 800.0f),
            96.0f,
            showStatus: false);

        Assert.AreEqual(new RectangleF(0.0f, 0.0f, 1000.0f, 800.0f), layout.Content);
        Assert.AreEqual(RectangleF.Empty, layout.Status);
    }
}
