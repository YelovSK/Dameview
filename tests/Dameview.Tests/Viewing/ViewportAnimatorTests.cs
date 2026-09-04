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
        var imagePosition = viewport.ViewportToImage(600.0f, 400.0f);

        Assert.IsTrue(animator.ZoomAt(600.0f, 400.0f, 120));
        timeProvider.Advance(TimeSpan.FromMilliseconds(16));
        Assert.IsTrue(animator.Update());

        Assert.IsGreaterThan(0.5f, viewport.Scale);
        Assert.IsLessThan(0.6f, viewport.Scale);
        Assert.AreEqual(imagePosition.X, viewport.ViewportToImage(600.0f, 400.0f).X, 0.001f);

        for (int frame = 0; frame < 100 && animator.IsAnimating; frame++)
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(16));
            animator.Update();
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
        viewport.ShowActualSize();
        var animator = new ViewportAnimator(viewport, timeProvider);

        animator.BeginPan(0.0f, 0.0f);
        timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        animator.PanTo(50.0f, 0.0f);
        timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        animator.PanTo(100.0f, 0.0f);
        float positionAtRelease = viewport.GetDestinationRectangle().X;

        Assert.IsTrue(animator.EndPan());
        timeProvider.Advance(TimeSpan.FromMilliseconds(16));
        Assert.IsTrue(animator.Update());

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
        viewport.ShowActualSize();
        var animator = new ViewportAnimator(viewport, timeProvider);

        animator.BeginPan(0.0f, 0.0f);
        timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        animator.PanTo(100.0f, 0.0f);
        Assert.IsTrue(animator.EndPan());
        float centerBeforeZoom = viewport.ViewportToImage(250.0f, 250.0f).X;

        Assert.IsTrue(animator.ZoomAt(250.0f, 250.0f, 120));
        timeProvider.Advance(TimeSpan.FromMilliseconds(16));
        animator.Update();

        Assert.AreEqual(centerBeforeZoom, viewport.ViewportToImage(250.0f, 250.0f).X, 0.001f);
    }

    [TestMethod]
    public void ActualSizeAnimatesScaleTowardThePointer()
    {
        var timeProvider = new ManualTimeProvider();
        var viewport = new ImageViewport(1000, 800);
        viewport.SetImageSize(2000, 1600);
        var animator = new ViewportAnimator(viewport, timeProvider);
        var imagePosition = viewport.ViewportToImage(600.0f, 400.0f);

        Assert.IsTrue(animator.ShowActualSizeAt(600.0f, 400.0f));
        timeProvider.Advance(TimeSpan.FromMilliseconds(16));
        Assert.IsTrue(animator.Update());

        Assert.IsGreaterThan(0.5f, viewport.Scale);
        Assert.IsLessThan(1.0f, viewport.Scale);
        Assert.AreEqual(imagePosition.X, viewport.ViewportToImage(600.0f, 400.0f).X, 0.001f);

        AdvanceUntilComplete(animator, timeProvider);

        Assert.AreEqual(ViewportMode.ActualSize, viewport.Mode);
        Assert.AreEqual(1.0f, viewport.Scale);
        Assert.AreEqual(imagePosition.X, viewport.ViewportToImage(600.0f, 400.0f).X, 0.001f);
        Assert.AreEqual(-600.0f, viewport.GetDestinationRectangle().X, 0.001f);
        Assert.AreEqual(-400.0f, viewport.GetDestinationRectangle().Y, 0.001f);
    }

    [TestMethod]
    public void FitAnimatesScaleAndRecentersAPannedImage()
    {
        var timeProvider = new ManualTimeProvider();
        var viewport = new ImageViewport(1000, 800);
        viewport.SetImageSize(2000, 1600);
        viewport.ShowActualSize();
        viewport.PanBy(200.0f, 100.0f);
        var animator = new ViewportAnimator(viewport, timeProvider);

        Assert.IsTrue(animator.Fit());
        AdvanceUntilComplete(animator, timeProvider);

        Assert.AreEqual(ViewportMode.Fit, viewport.Mode);
        Assert.AreEqual(0.5f, viewport.Scale);
        Assert.AreEqual(0.0f, viewport.GetDestinationRectangle().X, 0.001f);
        Assert.AreEqual(0.0f, viewport.GetDestinationRectangle().Y, 0.001f);
    }

    private static void AdvanceUntilComplete(
        ViewportAnimator animator,
        ManualTimeProvider timeProvider)
    {
        for (int frame = 0; frame < 100 && animator.IsAnimating; frame++)
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(16));
            animator.Update();
        }

        Assert.IsFalse(animator.IsAnimating);
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
