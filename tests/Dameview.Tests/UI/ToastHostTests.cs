using System.Drawing;
using Dameview.Notifications;
using Dameview.UI.Components;
using Dameview.UI.Foundation;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class ToastHostTests
{
    [TestMethod]
    public void AShownToastFadesInOverSeveralFrames()
    {
        var service = new ToastService();
        using var host = new ToastHost(service);
        Arrange(host);

        service.Notify("Could not save settings.", ToastSeverity.Error);
        Arrange(host);

        ToastView view = host.Children.OfType<ToastView>().Single();
        Assert.AreEqual(0.0f, view.Opacity, "A new toast starts fully transparent.");

        var frame = new UiUpdateContext(1.0 / 60.0);
        Assert.IsTrue(host.UpdateTree(frame), "An animating toast keeps asking for frames.");

        for (int index = 0; index < 60 && view.Opacity < 1.0f; index++)
        {
            host.UpdateTree(frame);
        }

        Assert.AreEqual(1.0f, view.Opacity);
    }

    [TestMethod]
    public void AnArrivingToastSlidesItsNeighbourUpInsteadOfJumpingIt()
    {
        var service = new ToastService();
        using var host = new ToastHost(service);
        service.Notify("First.", ToastSeverity.Warning);
        Arrange(host);
        ToastView first = host.Children.OfType<ToastView>().Single();
        float settled = first.Bounds.Y;
        Assert.AreEqual(0.0f, first.VisualOffset.Y);

        service.Notify("Second.", ToastSeverity.Warning);
        Arrange(host);

        Assert.IsTrue(first.Bounds.Y < settled, "The stack moved the first toast up.");
        Assert.AreEqual(
            settled - first.Bounds.Y,
            first.VisualOffset.Y,
            0.01f,
            "It is still drawn where it was, ready to slide.");

        var frame = new UiUpdateContext(1.0 / 60.0);
        for (int index = 0; index < 60 && first.VisualOffset.Y > 0.0f; index++)
        {
            host.UpdateTree(frame);
        }

        Assert.AreEqual(0.0f, first.VisualOffset.Y, "It ends up where layout put it.");
    }

    [TestMethod]
    public void ADismissedToastLeavesTheTreeOnceItHasFadedOut()
    {
        var service = new ToastService();
        using var host = new ToastHost(service);
        service.Notify("Copied.", ToastSeverity.Success);
        Arrange(host);

        var frame = new UiUpdateContext(1.0 / 60.0);
        for (int index = 0; index < 60 && host.Children.OfType<ToastView>().Single().Opacity < 1.0f; index++)
        {
            host.UpdateTree(frame);
        }

        service.Dismiss(service.Toasts.Single().Id);

        // The view outlives its message so that it can animate away, then goes.
        Assert.HasCount(1, host.Children.OfType<ToastView>().ToArray());
        for (int index = 0; index < 120 && host.Children.OfType<ToastView>().Any(); index++)
        {
            host.UpdateTree(frame);
        }

        Assert.IsEmpty(host.Children.OfType<ToastView>().ToArray(), "The faded toast was never removed.");
    }

    [TestMethod]
    public void AnExpiredToastLeavesWithoutBeingDismissed()
    {
        var time = new ManualTimeProvider();
        var service = new ToastService(time);
        using var host = new ToastHost(service);
        service.Notify("Saved.", ToastSeverity.Success);
        Arrange(host);

        time.Advance(ToastService.Lifetime + TimeSpan.FromSeconds(1));

        var frame = new UiUpdateContext(1.0 / 60.0);
        for (int index = 0; index < 120 && host.Children.OfType<ToastView>().Any(); index++)
        {
            host.UpdateTree(frame);
        }

        Assert.IsEmpty(service.Toasts.ToArray());
        Assert.IsEmpty(host.Children.OfType<ToastView>().ToArray());
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        internal void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;
    }

    private static void Arrange(ToastHost host)
    {
        // Measuring a toast shapes its message, which needs the root's shared text layouts.
        UiRoot root = host.Root ?? new UiRoot(host, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(800.0f, 600.0f));
    }
}
