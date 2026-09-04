using System.Drawing;
using Dameview.Viewing;

namespace Dameview.Tests.Viewing;

[TestClass]
public sealed class ImageViewportTests
{
    [TestMethod]
    public void FitCentersTheImageWithoutUpscaling()
    {
        var viewport = new ImageViewport(800, 600);

        viewport.SetImageSize(1600, 800);

        Assert.AreEqual(ViewportMode.Fit, viewport.Mode);
        Assert.AreEqual(0.5f, viewport.Scale);
        AssertRectangle(new RectangleF(0.0f, 100.0f, 800.0f, 400.0f), viewport.GetDestinationRectangle());
    }

    [TestMethod]
    public void ActualSizeUsesOneScreenPixelPerImagePixel()
    {
        var viewport = new ImageViewport(800, 600);
        viewport.SetImageSize(1600, 800);

        viewport.ShowActualSize();

        Assert.AreEqual(ViewportMode.ActualSize, viewport.Mode);
        Assert.AreEqual(1.0f, viewport.Scale);
        AssertRectangle(new RectangleF(-400.0f, -100.0f, 1600.0f, 800.0f), viewport.GetDestinationRectangle());
        AssertRectangle(new RectangleF(400.0f, 100.0f, 800.0f, 600.0f), viewport.GetVisibleImageRectangle());
    }

    [TestMethod]
    public void ZoomKeepsTheImagePositionUnderThePointer()
    {
        var viewport = new ImageViewport(1000, 800);
        viewport.SetImageSize(2000, 1600);
        PointF before = viewport.ViewportToImage(600.0f, 400.0f);

        viewport.ZoomAt(600.0f, 400.0f, 120);

        PointF after = viewport.ViewportToImage(600.0f, 400.0f);
        Assert.AreEqual(ViewportMode.Custom, viewport.Mode);
        Assert.AreEqual(before.X, after.X, 0.001f);
        Assert.AreEqual(before.Y, after.Y, 0.001f);
    }

    [TestMethod]
    public void PanningCannotMoveTheImagePastItsEdge()
    {
        var viewport = new ImageViewport(500, 500);
        viewport.SetImageSize(1000, 1000);
        viewport.ShowActualSize();

        viewport.PanBy(10_000.0f, 10_000.0f);

        AssertRectangle(new RectangleF(0.0f, 0.0f, 1000.0f, 1000.0f), viewport.GetDestinationRectangle());
    }

    [TestMethod]
    public void DraggingAFittedImageKeepsFitMode()
    {
        var viewport = new ImageViewport(500, 500);
        viewport.SetImageSize(1000, 500);

        viewport.PanBy(100.0f, 100.0f);

        Assert.AreEqual(ViewportMode.Fit, viewport.Mode);
    }

    [TestMethod]
    public void ResizingRecalculatesFitButPreservesActualSize()
    {
        var viewport = new ImageViewport(1000, 1000);
        viewport.SetImageSize(2000, 1000);

        viewport.SetViewportSize(500, 500);
        Assert.AreEqual(0.25f, viewport.Scale);

        viewport.ShowActualSize();
        viewport.SetViewportSize(700, 600);
        Assert.AreEqual(1.0f, viewport.Scale);
        Assert.AreEqual(ViewportMode.ActualSize, viewport.Mode);
    }

    private static void AssertRectangle(RectangleF expected, RectangleF actual)
    {
        Assert.AreEqual(expected.X, actual.X, 0.001f);
        Assert.AreEqual(expected.Y, actual.Y, 0.001f);
        Assert.AreEqual(expected.Width, actual.Width, 0.001f);
        Assert.AreEqual(expected.Height, actual.Height, 0.001f);
    }
}
