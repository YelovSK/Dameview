using System.Drawing;
using Dameview.UI.Layout;
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

    [TestMethod]
    public void VisibleRangeExpandsByRowsWhenThereAreMultipleColumns()
    {
        (int first, int last) = GalleryPanel.GetVisibleRange(
            100,
            scrollOffset: 0.0f,
            viewportHeight: 2 * GalleryPanel.ItemHeightDips,
            GalleryPanel.ItemHeightDips,
            columnCount: 2);

        Assert.AreEqual(0, first);
        Assert.AreEqual(6, last);
    }

    [TestMethod]
    public void GridHitTestMapsPointerToTheCorrectColumnAndRow()
    {
        int index = GalleryPanel.HitTestIndex(
            x: 117.0f,
            y: 160.0f,
            scrollOffset: 0.0f,
            count: 10,
            itemWidth: 100.0f,
            itemHeight: GalleryPanel.ItemHeightDips,
            columnCount: 2,
            itemGap: 8.0f);

        Assert.AreEqual(3, index);
        Assert.AreEqual(-1, GalleryPanel.HitTestIndex(
            x: 110.0f,
            y: 20.0f,
            scrollOffset: 0.0f,
            count: 10,
            itemWidth: 100.0f,
            itemHeight: GalleryPanel.ItemHeightDips,
            columnCount: 2,
            itemGap: 8.0f));
    }

    [TestMethod]
    public void HorizontalLayoutTransposesPointsAndBounds()
    {
        Assert.AreEqual(
            new PointF(20.0f, 10.0f),
            GalleryPanel.ToLayoutPoint(new PointF(10.0f, 20.0f), UiOrientation.Horizontal));
        Assert.AreEqual(
            new RectangleF(20.0f, 10.0f, 40.0f, 30.0f),
            GalleryPanel.FromLayoutBounds(
                new RectangleF(10.0f, 20.0f, 30.0f, 40.0f),
                UiOrientation.Horizontal));
    }

    [TestMethod]
    public void CenteredSelectionOffsetCentersWhenPossibleAndClampsAtTheEnds()
    {
        float viewportHeight = 3 * GalleryPanel.ItemHeightDips;

        Assert.AreEqual(0.0f, GalleryPanel.GetCenteredSelectionOffset(
            selectedIndex: 0,
            itemCount: 10,
            columnCount: 1,
            viewportHeight));
        Assert.AreEqual(4.5f * GalleryPanel.ItemHeightDips + 8.0f - viewportHeight / 2.0f,
            GalleryPanel.GetCenteredSelectionOffset(
                selectedIndex: 4,
                itemCount: 10,
                columnCount: 1,
                viewportHeight));
        Assert.AreEqual(10 * GalleryPanel.ItemHeightDips + 16.0f - viewportHeight,
            GalleryPanel.GetCenteredSelectionOffset(
                selectedIndex: 9,
                itemCount: 10,
                columnCount: 1,
                viewportHeight));
    }
}
