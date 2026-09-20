using System.Drawing;
using Dameview.Notifications;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Vortice.DirectWrite;
using static Vortice.DirectWrite.DWrite;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class ToastHostTests
{
    [TestMethod]
    public void AShownToastFadesInOverSeveralFrames()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        var service = new ToastService();
        using var host = new ToastHost(factory, service);
        Arrange(host);

        service.Notify("Could not save settings.", ToastSeverity.Error);
        Arrange(host);

        ToastView view = host.Children.OfType<ToastView>().Single();
        Assert.AreEqual(0.0f, view.Opacity, "A new toast starts fully transparent.");

        var frame = new UiUpdateContext(1.0 / 60.0);
        Assert.IsTrue(host.UpdateTree(frame));
        Assert.IsTrue(
            view.Opacity is > 0.0f and < 1.0f,
            $"One frame should move it part of the way, not all: {view.Opacity}");

        for (int index = 0; index < 60 && view.Opacity < 1.0f; index++)
        {
            host.UpdateTree(frame);
        }

        Assert.AreEqual(1.0f, view.Opacity);
    }

    [TestMethod]
    public void AnArrivingToastSlidesItsNeighbourUpInsteadOfJumpingIt()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        var service = new ToastService();
        using var host = new ToastHost(factory, service);
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

    private static void Arrange(ToastHost host)
    {
        host.Measure(new SizeF(800.0f, 600.0f));
        host.Arrange(new RectangleF(0.0f, 0.0f, 800.0f, 600.0f));
    }
}
