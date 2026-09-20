using System.Drawing;
using Dameview.Viewing;

namespace Dameview.Tests.Viewing;

[TestClass]
public sealed class ViewportAnimatorTests
{
    [TestMethod]
    public void ZoomApproachesItsTargetAndKeepsThePointerAnchored()
    {
        var timeProvider = new ManualTimeProvider();
        var viewport = new ImageViewport(1000, 800);
        viewport.SetImageSize(2000, 1600);
        var animator = new ViewportAnimator(viewport, timeProvider);
        PointF imagePosition = viewport.ViewportToImage(600.0f, 400.0f);

        Assert.IsTrue(animator.ZoomAt(600.0f, 400.0f, 120));
        Assert.IsTrue(animator.Update(0.016));

        Assert.IsGreaterThan(0.5f, viewport.Scale);
        Assert.IsLessThan(0.6f, viewport.Scale);
        Assert.AreEqual(imagePosition.X, viewport.ViewportToImage(600.0f, 400.0f).X, 0.001f);

        for (int frame = 0; frame < 100 && animator.IsAnimating; frame++)
        {
            animator.Update(0.016);
        }

        Assert.IsFalse(animator.IsAnimating);
        Assert.AreEqual(0.6f, viewport.Scale, 0.001f);
    }

    [TestMethod]
    public void ReleasingAPanContinuesWithMomentum()
    {
        var timeProvider = new ManualTimeProvider();
        var viewport = new ImageViewport(500, 500);
        viewport.SetImageSize(2000, 2000);
        viewport.SetActualSizeAt(viewport.ViewportCenter.X, viewport.ViewportCenter.Y, viewport.ImageCenter);
        var animator = new ViewportAnimator(viewport, timeProvider);

        animator.BeginPan(0.0f, 0.0f);
        timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        animator.PanTo(50.0f, 0.0f);
        timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        animator.PanTo(100.0f, 0.0f);
        float positionAtRelease = viewport.GetDestinationRectangle().X;

        Assert.IsTrue(animator.EndPan());
        Assert.IsTrue(animator.Update(0.016));

        Assert.IsTrue(viewport.GetDestinationRectangle().X > positionAtRelease);
    }

    [TestMethod]
    public void BeginningAPanCancelsZoomAnimation()
    {
        var viewport = new ImageViewport(1000, 800);
        viewport.SetImageSize(2000, 1600);
        var animator = new ViewportAnimator(viewport, new ManualTimeProvider());

        Assert.IsTrue(animator.ZoomAt(600.0f, 400.0f, 120));

        animator.BeginPan(600.0f, 400.0f);

        Assert.IsFalse(animator.IsAnimating);
    }

    [TestMethod]
    public void BeginningZoomCancelsPanMomentum()
    {
        var timeProvider = new ManualTimeProvider();
        var viewport = new ImageViewport(500, 500);
        viewport.SetImageSize(2000, 2000);
        viewport.SetActualSizeAt(viewport.ViewportCenter.X, viewport.ViewportCenter.Y, viewport.ImageCenter);
        var animator = new ViewportAnimator(viewport, timeProvider);

        animator.BeginPan(0.0f, 0.0f);
        timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        animator.PanTo(100.0f, 0.0f);
        Assert.IsTrue(animator.EndPan());
        float centerBeforeZoom = viewport.ViewportToImage(250.0f, 250.0f).X;

        Assert.IsTrue(animator.ZoomAt(250.0f, 250.0f, 120));
        animator.Update(0.016);

        Assert.AreEqual(centerBeforeZoom, viewport.ViewportToImage(250.0f, 250.0f).X, 0.001f);
    }

    [TestMethod]
    public void ReversingAnActivePanDoesNotAmplifyItsFirstPointerMove()
    {
        var timeProvider = new ManualTimeProvider();
        var viewport = new ImageViewport(500, 500);
        viewport.SetImageSize(2000, 2000);
        viewport.SetActualSizeAt(viewport.ViewportCenter.X, viewport.ViewportCenter.Y, viewport.ImageCenter);
        var animator = new ViewportAnimator(viewport, timeProvider);

        animator.BeginPan(0.0f, 0.0f);
        timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        animator.PanTo(100.0f, 0.0f);
        Assert.IsTrue(animator.EndPan());
        animator.Update(0.016);

        animator.BeginPan(100.0f, 0.0f);
        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        animator.PanTo(98.0f, 0.0f);
        float positionAtRelease = viewport.GetDestinationRectangle().X;

        Assert.IsTrue(animator.EndPan());
        animator.Update(0.016);
        Assert.AreEqual(positionAtRelease, viewport.GetDestinationRectangle().X, 1.0f);
    }

    [TestMethod]
    public void ActualSizeAnimatesScaleTowardThePointer()
    {
        var timeProvider = new ManualTimeProvider();
        var viewport = new ImageViewport(1000, 800);
        viewport.SetImageSize(2000, 1600);
        var animator = new ViewportAnimator(viewport, timeProvider);
        PointF imagePosition = viewport.ViewportToImage(600.0f, 400.0f);

        Assert.IsTrue(animator.ShowActualSizeAt(600.0f, 400.0f));
        Assert.IsTrue(animator.Update(0.016));

        Assert.IsGreaterThan(0.5f, viewport.Scale);
        Assert.IsLessThan(1.0f, viewport.Scale);
        Assert.AreEqual(imagePosition.X, viewport.ViewportToImage(600.0f, 400.0f).X, 0.001f);

        AdvanceUntilComplete(animator);

        Assert.AreEqual(ViewportMode.ActualSize, viewport.Mode);
        Assert.AreEqual(1.0f, viewport.Scale);
        Assert.AreEqual(imagePosition.X, viewport.ViewportToImage(600.0f, 400.0f).X, 0.001f);
        Assert.AreEqual(-600.0f, viewport.GetDestinationRectangle().X, 0.001f);
        Assert.AreEqual(-400.0f, viewport.GetDestinationRectangle().Y, 0.001f);
    }

    [TestMethod]
    public void ZoomingMovesEveryDestinationEdgeWithTheSameProgress()
    {
        // Zooming in to actual size, and back out to fit. A transform that skewed the
        // destination instead of scaling it would move one edge ahead of the others.
        AssertEdgesMoveTogether(
            _ => { },
            animator => animator.ShowActualSizeAt(750.0f, 600.0f),
            new RectangleF(-750.0f, -200.0f, 2000.0f, 1000.0f));
        AssertEdgesMoveTogether(
            viewport => viewport.SetActualSizeAt(750.0f, 600.0f, new PointF(1500.0f, 900.0f)),
            animator => animator.Fit(),
            new RectangleF(0.0f, 150.0f, 1000.0f, 500.0f));
    }

    [TestMethod]
    public void TogglingSwapsBetweenFitAndActualSize()
    {
        var viewport = new ImageViewport(1000, 800);
        viewport.SetImageSize(2000, 1000);
        var animator = new ViewportAnimator(viewport, new ManualTimeProvider());

        Assert.IsTrue(animator.ToggleFitAndActualSizeAt(500.0f, 400.0f));
        animator.Update(0.0, animationsEnabled: false);
        Assert.AreEqual(ViewportMode.ActualSize, viewport.Mode);

        Assert.IsTrue(animator.ToggleFitAndActualSizeAt(500.0f, 400.0f));
        animator.Update(0.0, animationsEnabled: false);
        Assert.AreEqual(ViewportMode.Fit, viewport.Mode);
    }

    [TestMethod]
    public void TogglingMidAnimationReadsWhereTheViewportIsHeaded()
    {
        var viewport = new ImageViewport(1000, 800);
        viewport.SetImageSize(2000, 1000);
        var animator = new ViewportAnimator(viewport, new ManualTimeProvider());

        // Still animating toward actual size, so a second toggle must turn back to fit
        // rather than read the viewport's current mode and start over.
        Assert.IsTrue(animator.ToggleFitAndActualSizeAt(500.0f, 400.0f));
        Assert.IsTrue(animator.Update(0.016));
        Assert.IsTrue(animator.ToggleFitAndActualSizeAt(500.0f, 400.0f));
        animator.Update(0.0, animationsEnabled: false);

        Assert.AreEqual(ViewportMode.Fit, viewport.Mode);
    }

    [TestMethod]
    public void TogglingAPannedImageFitsItRatherThanZoomingFurther()
    {
        var viewport = new ImageViewport(1000, 800);
        viewport.SetImageSize(2000, 1000);
        viewport.SetActualSizeAt(500.0f, 400.0f, viewport.ImageCenter);
        viewport.PanBy(50.0f, 20.0f);
        Assert.AreEqual(ViewportMode.Custom, viewport.Mode);
        var animator = new ViewportAnimator(viewport, new ManualTimeProvider());

        Assert.IsTrue(animator.ToggleFitAndActualSizeAt(500.0f, 400.0f));
        animator.Update(0.0, animationsEnabled: false);

        Assert.AreEqual(ViewportMode.Fit, viewport.Mode);
    }

    private static void AssertEdgesMoveTogether(
        Action<ImageViewport> arrange,
        Func<ViewportAnimator, bool> begin,
        RectangleF target)
    {
        var viewport = new ImageViewport(1000, 800);
        viewport.SetImageSize(2000, 1000);
        arrange(viewport);
        var animator = new ViewportAnimator(viewport, new ManualTimeProvider());
        RectangleF start = viewport.GetDestinationRectangle();

        Assert.IsTrue(begin(animator));
        Assert.IsTrue(animator.Update(0.016));

        RectangleF current = viewport.GetDestinationRectangle();
        float widthProgress = GetProgress(start.Width, current.Width, target.Width);
        Assert.AreEqual(widthProgress, GetProgress(start.X, current.X, target.X), 0.001f);
        Assert.AreEqual(widthProgress, GetProgress(start.Y, current.Y, target.Y), 0.001f);
        Assert.AreEqual(widthProgress, GetProgress(start.Height, current.Height, target.Height), 0.001f);
    }

    [TestMethod]
    public void FitAnimatesScaleAndRecentersAPannedImage()
    {
        var timeProvider = new ManualTimeProvider();
        var viewport = new ImageViewport(1000, 800);
        viewport.SetImageSize(2000, 1600);
        viewport.SetActualSizeAt(viewport.ViewportCenter.X, viewport.ViewportCenter.Y, viewport.ImageCenter);
        viewport.PanBy(200.0f, 100.0f);
        var animator = new ViewportAnimator(viewport, timeProvider);

        Assert.IsTrue(animator.Fit());
        AdvanceUntilComplete(animator);

        Assert.AreEqual(ViewportMode.Fit, viewport.Mode);
        Assert.AreEqual(0.5f, viewport.Scale);
        Assert.AreEqual(0.0f, viewport.GetDestinationRectangle().X, 0.001f);
        Assert.AreEqual(0.0f, viewport.GetDestinationRectangle().Y, 0.001f);
    }

    [TestMethod]
    public void DisabledAnimationsCompleteZoomImmediately()
    {
        var viewport = new ImageViewport(1000, 800);
        viewport.SetImageSize(2000, 1600);
        var animator = new ViewportAnimator(viewport, new ManualTimeProvider());

        Assert.IsTrue(animator.ZoomAt(600.0f, 400.0f, 120));
        Assert.IsFalse(animator.Update(0.0, animationsEnabled: false));

        Assert.IsFalse(animator.IsAnimating);
        Assert.AreEqual(0.6f, viewport.Scale, 0.001f);
    }

    [TestMethod]
    public void DisabledAnimationsCancelPanMomentum()
    {
        var timeProvider = new ManualTimeProvider();
        var viewport = new ImageViewport(500, 500);
        viewport.SetImageSize(2000, 2000);
        viewport.SetActualSizeAt(viewport.ViewportCenter.X, viewport.ViewportCenter.Y, viewport.ImageCenter);
        var animator = new ViewportAnimator(viewport, timeProvider);

        animator.BeginPan(0.0f, 0.0f);
        timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        animator.PanTo(100.0f, 0.0f);
        Assert.IsTrue(animator.EndPan());
        RectangleF positionAtRelease = viewport.GetDestinationRectangle();

        Assert.IsFalse(animator.Update(0.0, animationsEnabled: false));
        Assert.IsFalse(animator.IsAnimating);
        Assert.AreEqual(positionAtRelease, viewport.GetDestinationRectangle());
    }

    private static void AdvanceUntilComplete(ViewportAnimator animator)
    {
        for (int frame = 0; frame < 100 && animator.IsAnimating; frame++)
        {
            animator.Update(0.016);
        }

        Assert.IsFalse(animator.IsAnimating);
    }

    private static float GetProgress(float start, float current, float target)
    {
        return (current - start) / (target - start);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            return _timestamp;
        }

        internal void Advance(TimeSpan elapsed)
        {
            _timestamp += elapsed.Ticks;
        }
    }
}
