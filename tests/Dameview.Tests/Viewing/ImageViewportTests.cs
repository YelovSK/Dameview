using System.Drawing;
using System.Numerics;
using Dameview.Imaging;
using Dameview.Viewing;

namespace Dameview.Tests.Viewing;

[TestClass]
public sealed class ImageViewportTests
{
    [TestMethod]
    public void FitCentersTheImageWithoutUpscaling()
    {
        var viewport = new ImageViewport(new Size(800, 600));

        viewport.SetImageSize(new Size(1600, 800));

        Assert.AreEqual(ViewportMode.Fit, viewport.Mode);
        Assert.AreEqual(0.5f, viewport.Scale);
        AssertRectangle(new RectangleF(0.0f, 100.0f, 800.0f, 400.0f), viewport.GetDestinationRectangle());
    }

    [TestMethod]
    public void FitUpscalesASmallImageWhileActualSizeRemainsAvailable()
    {
        var viewport = new ImageViewport(new Size(800, 600));
        viewport.SetImageSize(new Size(400, 200));

        Assert.AreEqual(2.0f, viewport.Scale);
        AssertRectangle(new RectangleF(0.0f, 100.0f, 800.0f, 400.0f), viewport.GetDestinationRectangle());

        viewport.SetActualSizeAt(viewport.ViewportCenter, viewport.ImageCenter);

        Assert.AreEqual(ViewportMode.ActualSize, viewport.Mode);
        Assert.AreEqual(1.0f, viewport.Scale);
        AssertRectangle(new RectangleF(200.0f, 200.0f, 400.0f, 200.0f), viewport.GetDestinationRectangle());
    }

    [TestMethod]
    public void ActualSizeUsesOneScreenPixelPerImagePixel()
    {
        var viewport = new ImageViewport(new Size(800, 600));
        viewport.SetImageSize(new Size(1600, 800));

        viewport.SetActualSizeAt(viewport.ViewportCenter, viewport.ImageCenter);

        Assert.AreEqual(ViewportMode.ActualSize, viewport.Mode);
        Assert.AreEqual(1.0f, viewport.Scale);
        AssertRectangle(new RectangleF(-400.0f, -100.0f, 1600.0f, 800.0f), viewport.GetDestinationRectangle());
    }

    [TestMethod]
    public void ZoomKeepsTheImagePositionUnderThePointer()
    {
        var viewport = new ImageViewport(new Size(1000, 800));
        viewport.SetImageSize(new Size(2000, 1600));
        PointF before = viewport.ViewportToImage(new PointF(600.0f, 400.0f));

        viewport.SetScaleAt(viewport.GetZoomScale(viewport.Scale, 120), new PointF(600.0f, 400.0f), before);

        PointF after = viewport.ViewportToImage(new PointF(600.0f, 400.0f));
        Assert.AreEqual(ViewportMode.Custom, viewport.Mode);
        Assert.AreEqual(before.X, after.X, 0.001f);
        Assert.AreEqual(before.Y, after.Y, 0.001f);
    }

    [TestMethod]
    public void PanningCannotMoveTheImagePastItsEdge()
    {
        var viewport = new ImageViewport(new Size(500, 500));
        viewport.SetImageSize(new Size(1000, 1000));
        viewport.SetActualSizeAt(viewport.ViewportCenter, viewport.ImageCenter);

        viewport.PanBy(new Vector2(10_000.0f, 10_000.0f));

        AssertRectangle(new RectangleF(0.0f, 0.0f, 1000.0f, 1000.0f), viewport.GetDestinationRectangle());
    }

    [TestMethod]
    public void DraggingAFittedImageKeepsFitMode()
    {
        var viewport = new ImageViewport(new Size(500, 500));
        viewport.SetImageSize(new Size(1000, 500));

        viewport.PanBy(new Vector2(100.0f, 100.0f));

        Assert.AreEqual(ViewportMode.Fit, viewport.Mode);
    }

    [TestMethod]
    public void ResizingRecalculatesFitButPreservesActualSize()
    {
        var viewport = new ImageViewport(new Size(1000, 1000));
        viewport.SetImageSize(new Size(2000, 1000));

        viewport.SetViewportSize(new Size(500, 500));
        Assert.AreEqual(0.25f, viewport.Scale);

        viewport.SetActualSizeAt(viewport.ViewportCenter, viewport.ImageCenter);
        viewport.SetViewportSize(new Size(700, 600));
        Assert.AreEqual(1.0f, viewport.Scale);
        Assert.AreEqual(ViewportMode.ActualSize, viewport.Mode);
    }

    [TestMethod]
    public void FitsTheImageAsItIsShown()
    {
        var viewport = new ImageViewport(new Size(800, 600));

        viewport.SetImageSize(new Size(2000, 1000), ImageOrientation.FromExif(6));

        Assert.AreEqual(0.3f, viewport.Scale, 0.0001f);
        AssertRectangle(new RectangleF(250.0f, 0.0f, 300.0f, 600.0f), viewport.GetDestinationRectangle());
        AssertMapsTo(viewport, new Vector2(0.0f, 0.0f), new PointF(550.0f, 0.0f));
    }

    [TestMethod]
    public void TurningAFittedImageFitsItAgain()
    {
        var viewport = new ImageViewport(new Size(800, 600));
        viewport.SetImageSize(new Size(2000, 1000));

        viewport.SetOrientation(viewport.Orientation.Rotate(1));

        Assert.AreEqual(ViewportMode.Fit, viewport.Mode);
        AssertRectangle(new RectangleF(250.0f, 0.0f, 300.0f, 600.0f), viewport.GetDestinationRectangle());
    }

    [TestMethod]
    public void TurningAZoomedImageKeepsTheSameSpotInTheMiddle()
    {
        var viewport = new ImageViewport(new Size(800, 600));
        viewport.SetImageSize(new Size(2000, 1000));
        viewport.SetScaleAt(2.0f, viewport.ViewportCenter, new PointF(700.0f, 400.0f));

        viewport.SetOrientation(viewport.Orientation.Rotate(1).FlipHorizontal());

        Assert.AreEqual(2.0f, viewport.Scale);
        AssertMapsTo(viewport, new Vector2(700.0f, 400.0f), viewport.ViewportCenter);
    }

    private static void AssertMapsTo(ImageViewport viewport, Vector2 storedPoint, PointF expected)
    {
        var actual = Vector2.Transform(storedPoint, viewport.GetImageTransform(new SizeF(2000.0f, 1000.0f)));
        Assert.AreEqual(expected.X, actual.X, 0.001f);
        Assert.AreEqual(expected.Y, actual.Y, 0.001f);
    }

    private static void AssertRectangle(RectangleF expected, RectangleF actual)
    {
        Assert.AreEqual(expected.X, actual.X, 0.001f);
        Assert.AreEqual(expected.Y, actual.Y, 0.001f);
        Assert.AreEqual(expected.Width, actual.Width, 0.001f);
        Assert.AreEqual(expected.Height, actual.Height, 0.001f);
    }
    // Panning and zooming both clamp the centre, and the two clamps are written separately.
    // A seeded sweep catches a combination that a hand-picked case would miss; the seed keeps
    // any failure reproducible and the message names the operation that broke it.
    [TestMethod]
    public void PanningAndZoomingNeverUncoverDeadSpace()
    {
        foreach ((int imageWidth, int imageHeight) in new[] { (2000, 1000), (300, 240), (4000, 4000), (600, 5000) })
        {
            var viewport = new ImageViewport(new Size(1000, 800));
            viewport.SetImageSize(new Size(imageWidth, imageHeight));
            var random = new Random(20260920);

            for (int step = 0; step < 400; step++)
            {
                string operation;
                if (step % 3 == 0)
                {
                    int delta = (random.Next(2) == 0 ? -1 : 1) * 120;
                    float scale = viewport.GetZoomScale(viewport.Scale, delta);
                    float x = (float)random.NextDouble() * 1000.0f;
                    float y = (float)random.NextDouble() * 800.0f;
                    viewport.SetScaleAt(scale, new PointF(x, y), viewport.ViewportToImage(new PointF(x, y)));
                    operation = $"zoom {delta} at {x}x{y}";
                }
                else
                {
                    float dx = ((float)random.NextDouble() - 0.5f) * 4000.0f;
                    float dy = ((float)random.NextDouble() - 0.5f) * 4000.0f;
                    viewport.PanBy(new Vector2(dx, dy));
                    operation = $"pan {dx}x{dy}";
                }

                AssertNoDeadSpace(viewport, $"{imageWidth}x{imageHeight} step {step}: {operation}");
            }
        }
    }

    private static void AssertNoDeadSpace(ImageViewport viewport, string because)
    {
        RectangleF destination = viewport.GetDestinationRectangle();
        const float tolerance = 0.01f;

        if (destination.Width >= 1000.0f)
        {
            Assert.IsTrue(destination.Left <= tolerance, $"gap on the left: {destination} ({because})");
            Assert.IsTrue(destination.Right >= 1000.0f - tolerance, $"gap on the right: {destination} ({because})");
        }
        else
        {
            Assert.AreEqual((1000.0f - destination.Width) / 2.0f, destination.Left, tolerance, because);
        }

        if (destination.Height >= 800.0f)
        {
            Assert.IsTrue(destination.Top <= tolerance, $"gap on top: {destination} ({because})");
            Assert.IsTrue(destination.Bottom >= 800.0f - tolerance, $"gap below: {destination} ({because})");
        }
        else
        {
            Assert.AreEqual((800.0f - destination.Height) / 2.0f, destination.Top, tolerance, because);
        }
    }

}
