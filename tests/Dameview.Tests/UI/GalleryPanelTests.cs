using Dameview.UI.Panels;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class GalleryPanelTests
{
    [TestMethod]
    public void VisibleRangeOnlyIncludesTheViewportAndOverscan()
    {
        (int first, int last) = GalleryPanel.GetVisibleRange(
            100,
            scrollOffset: 10 * GalleryPanel.ItemHeightDips,
            viewportHeight: 3 * GalleryPanel.ItemHeightDips,
            GalleryPanel.ItemHeightDips);

        Assert.AreEqual(8, first);
        Assert.AreEqual(14, last);
    }

    [TestMethod]
    public void HitTestAccountsForScrollAndPanelPadding()
    {
        int index = GalleryPanel.HitTestIndex(
            y: 20.0f,
            scrollOffset: 2 * GalleryPanel.ItemHeightDips,
            count: 10,
            GalleryPanel.ItemHeightDips);

        Assert.AreEqual(2, index);
        Assert.AreEqual(-1, GalleryPanel.HitTestIndex(0.0f, 0.0f, 10, GalleryPanel.ItemHeightDips));
    }
}
