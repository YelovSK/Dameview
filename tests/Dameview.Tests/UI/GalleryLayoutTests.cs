using System.Drawing;
using Dameview.Settings;
using Dameview.UI.Layout;
using Dameview.UI.Panels;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class GalleryLayoutTests
{
    // Wide enough for one column at the medium size, and tall enough for three rows.
    private const float SingleColumnWidth = 160.0f;
    private const float RowHeight = 142.0f;

    private static GalleryLayout Create(
        int count,
        float width = SingleColumnWidth,
        float height = 3 * RowHeight,
        UiOrientation orientation = UiOrientation.Vertical) =>
        new(
            orientation == UiOrientation.Vertical
                ? new SizeF(width, height)
                : new SizeF(height, width),
            orientation,
            GalleryThumbnailSize.Medium,
            count);

    [TestMethod]
    public void VisibleRangeOnlyIncludesTheViewportAndOverscan()
    {
        GalleryLayout layout = Create(100);

        (int first, int last) = layout.GetVisibleRange(10 * RowHeight);

        Assert.AreEqual(1, layout.ColumnCount);
        Assert.AreEqual(8, first);
        Assert.AreEqual(14, last);
    }

    [TestMethod]
    public void VisibleRangeIsEmptyWithoutItems()
    {
        Assert.AreEqual((0, 0), Create(0).GetVisibleRange(0.0f));
    }

    [TestMethod]
    public void VisibleRangeExpandsByRowsWhenThereAreMultipleColumns()
    {
        GalleryLayout layout = Create(100, width: 300.0f, height: 2 * RowHeight);

        (int first, int last) = layout.GetVisibleRange(0.0f);

        Assert.AreEqual(2, layout.ColumnCount);
        Assert.AreEqual(0, first);
        Assert.AreEqual(6, last);
    }

    [TestMethod]
    public void HitTestAccountsForScrollAndPanelPadding()
    {
        GalleryLayout layout = Create(10);

        Assert.AreEqual(2, layout.HitTest(new PointF(20.0f, 20.0f), 2 * RowHeight));
        // The top-left corner is panel padding, not the first item.
        Assert.AreEqual(-1, layout.HitTest(new PointF(0.0f, 0.0f), 0.0f));
    }

    [TestMethod]
    public void HitTestMapsThePointerToTheCorrectColumnAndRow()
    {
        GalleryLayout layout = Create(10, width: 300.0f);

        Assert.AreEqual(2, layout.ColumnCount);
        Assert.AreEqual(3, layout.HitTest(new PointF(200.0f, 160.0f), 0.0f));
    }

    [TestMethod]
    public void HitTestMissesTheGapBetweenColumns()
    {
        GalleryLayout layout = Create(10, width: 300.0f);
        RectangleF first = layout.GetItemBounds(0, 0.0f);

        // Two dips past the first column, inside the spacing before the second.
        Assert.AreEqual(-1, layout.HitTest(new PointF(first.Right + 2.0f, 20.0f), 0.0f));
    }

    [TestMethod]
    public void HitTestMissesTheGapBetweenRows()
    {
        GalleryLayout layout = Create(10);
        RectangleF first = layout.GetItemBounds(0, 0.0f);

        // Two dips past the first row, inside the spacing before the second.
        Assert.AreEqual(-1, layout.HitTest(new PointF(20.0f, first.Bottom + 2.0f), 0.0f));
        Assert.AreEqual(1, layout.HitTest(new PointF(20.0f, first.Bottom + 6.0f), 0.0f));
    }

    [TestMethod]
    public void HitTestMissesTheScrollbarGutter()
    {
        GalleryLayout layout = Create(10);

        Assert.AreEqual(
            -1,
            layout.HitTest(new PointF(SingleColumnWidth - 1.0f, 20.0f), 0.0f));
    }

    [TestMethod]
    public void HitTestFindsNothingPastTheLastItem()
    {
        GalleryLayout layout = Create(2);

        Assert.AreEqual(-1, layout.HitTest(new PointF(20.0f, 20.0f), 5 * RowHeight));
    }

    [TestMethod]
    public void CenteredOffsetCentersWhenPossibleAndClampsAtTheEnds()
    {
        GalleryLayout layout = Create(10);
        float viewport = layout.ViewportLength;

        Assert.AreEqual(0.0f, layout.GetCenteredOffset(0));
        Assert.AreEqual(
            (4.5f * RowHeight) + 8.0f - (viewport / 2.0f),
            layout.GetCenteredOffset(4));
        Assert.AreEqual(layout.MaximumScrollOffset, layout.GetCenteredOffset(9));
    }

    [TestMethod]
    public void RevealOffsetOnlyMovesWhenTheRowIsOutOfView()
    {
        GalleryLayout layout = Create(10);
        float visible = layout.GetRevealOffset(1, 0.0f);

        Assert.AreEqual(0.0f, visible, "A row already in view should not scroll.");
        Assert.AreEqual(8.0f, layout.GetRevealOffset(0, 50.0f), "Scrolls back to the first row.");
        Assert.IsTrue(layout.GetRevealOffset(9, 0.0f) > 0.0f, "Scrolls forward to a later row.");
    }

    [TestMethod]
    public void HorizontalLayoutTransposesCoordinates()
    {
        GalleryLayout vertical = Create(10);
        GalleryLayout horizontal = Create(10, orientation: UiOrientation.Horizontal);

        // The breadth a column grows to is shared; only the fixed extent differs, because
        // each orientation takes it from a different column of the thumbnail size table.
        Assert.AreEqual(vertical.ItemSize.Width, horizontal.ItemSize.Height);
        Assert.AreEqual(
            vertical.HitTest(new PointF(20.0f, 30.0f), 0.0f),
            horizontal.HitTest(new PointF(30.0f, 20.0f), 0.0f));

        RectangleF item = horizontal.GetItemBounds(1, 0.0f);
        Assert.AreEqual(1, horizontal.HitTest(new PointF(item.X + 1.0f, item.Y + 1.0f), 0.0f));
    }

    [TestMethod]
    public void ContentLengthGrowsWithRowsAndBoundsTheScrollOffset()
    {
        GalleryLayout layout = Create(10);

        Assert.AreEqual(10, layout.RowCount);
        Assert.AreEqual((10 * RowHeight) + 16.0f, layout.ContentLength);
        Assert.AreEqual(layout.ContentLength - layout.ViewportLength, layout.MaximumScrollOffset);
    }

    [TestMethod]
    public void ATooNarrowPanelStillLaysOutOneColumn()
    {
        GalleryLayout layout = Create(10, width: 10.0f);

        Assert.AreEqual(1, layout.ColumnCount);
        Assert.AreEqual(0.0f, layout.ItemSize.Width);
    }
    // A sweep rather than a handful of sizes, because the arithmetic that places an item and
    // the arithmetic that hit-tests one are written separately and must agree everywhere.
    [TestMethod]
    public void EveryVisibleItemIsFoundAtItsOwnCentre()
    {
        foreach (UiOrientation orientation in Enum.GetValues<UiOrientation>())
        {
            foreach (GalleryThumbnailSize size in Enum.GetValues<GalleryThumbnailSize>())
            {
                foreach (float width in new[] { 140.0f, 213.0f, 300.0f, 512.0f, 1000.0f })
                {
                    foreach (int count in new[] { 1, 7, 40 })
                    {
                        AssertCentresHitTheirOwnItem(orientation, size, width, count);
                    }
                }
            }
        }
    }

    private static void AssertCentresHitTheirOwnItem(
        UiOrientation orientation,
        GalleryThumbnailSize size,
        float width,
        int count)
    {
        SizeF panel = orientation == UiOrientation.Vertical
            ? new SizeF(width, 600.0f)
            : new SizeF(600.0f, width);
        var layout = new GalleryLayout(panel, orientation, size, count);
        const float scrollOffset = 37.0f;
        (int first, int lastExclusive) = layout.GetVisibleRange(scrollOffset);

        for (int index = first; index < lastExclusive; index++)
        {
            RectangleF item = layout.GetItemBounds(index, scrollOffset);
            var centre = new PointF(item.X + (item.Width / 2.0f), item.Y + (item.Height / 2.0f));
            if (!new RectangleF(PointF.Empty, panel).Contains(centre))
            {
                continue;
            }

            Assert.AreEqual(
                index,
                layout.HitTest(centre, scrollOffset),
                $"{orientation} {size} width={width} count={count} index={index} centre={centre}");
        }
    }

}
