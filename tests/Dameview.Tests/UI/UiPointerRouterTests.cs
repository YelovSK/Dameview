using System.Drawing;
using Dameview.UI;
using Dameview.UI.Components;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class UiPointerRouterTests
{
    private static readonly RectangleF Bounds = new(100, 200, 80, 40);

    [TestMethod]
    public void RepaintDoesNotConsumeOrCaptureAnEvent()
    {
        var router = new UiPointerRouter();
        var child = new Element(_ => new UiPointerResult(NeedsRepaint: true));
        var input = new UiPointerEvent(UiPointerEventKind.Moved, PointF.Empty);

        Assert.AreEqual(default(UiPointerResult), router.Route(child, Bounds, input));
        Assert.HasCount(0, child.Events);
        UiPointerResult result = router.Route(child, Bounds, input, observeOutside: true);

        Assert.IsTrue(result.NeedsRepaint);
        Assert.IsFalse(result.Consumed);
        Assert.IsNull(router.Captured);
        Assert.AreEqual(new PointF(-100, -200), child.Events[0].Position);
    }

    [TestMethod]
    public void ConsumingAPressDoesNotImplicitlyCapture()
    {
        var router = new UiPointerRouter();
        var child = new Element(_ => new UiPointerResult(Consumed: true));
        router.Route(child, Bounds, PressAt(110, 210));
        Assert.IsNull(router.Captured);
    }

    [TestMethod]
    public void CapturedReleaseUsesCurrentBoundsEvenOutsideTheChild()
    {
        var router = new UiPointerRouter();
        var child = new Element(input =>
        {
            if (input.Kind == UiPointerEventKind.Released)
            {
                Assert.IsNull(router.Captured);
            }

            return new UiPointerResult(Consumed: true, CapturePointer: input.Kind == UiPointerEventKind.Pressed);
        });
        router.Route(child, Bounds, PressAt(110, 210));
        Assert.AreSame(child, router.Captured);

        UiPointerResult result = router.DispatchCaptured(
            new UiPointerEvent(UiPointerEventKind.Released, new PointF(250, 300), PointerButton.Primary),
            new RectangleF(120, 230, 80, 40));

        Assert.IsTrue(result.Consumed);
        Assert.AreEqual(new PointF(130, 70), child.Events[1].Position);
        Assert.IsNull(router.Captured);
    }

    [TestMethod]
    public void CancellationPropagatesThroughNestedCaptureWithoutClicking()
    {
        int clicks = 0;
        var button = new ButtonElement(() => clicks++);
        var container = new Container(button);
        var root = new UiPointerRouter();
        root.Route(container, Bounds, PressAt(110, 210));
        Assert.AreSame(container, root.Captured);
        Assert.AreSame(button, container.Router.Captured);

        root.Cancel();
        root.Cancel();
        Assert.IsNull(root.Captured);
        Assert.IsNull(container.Router.Captured);
        Assert.AreEqual(0, clicks);
        Assert.HasCount(2, button.Events);
        Assert.AreEqual(UiPointerEventKind.Cancelled, button.Events[1].Kind);

        // A late mouse-up after cancellation cannot activate the old press.
        root.Route(container, Bounds,
            new UiPointerEvent(UiPointerEventKind.Released, new PointF(110, 210), PointerButton.Primary));
        Assert.AreEqual(0, clicks);
    }

    [TestMethod]
    public void ReleaseClearsBothCapturesBeforeInvokingACommand()
    {
        var root = new UiPointerRouter();
        Container? container = null;
        int clicks = 0;
        var button = new ButtonElement(() =>
        {
            Assert.IsNull(root.Captured);
            Assert.IsNull(container!.Router.Captured);
            root.Cancel(); // Content replacement during a command is safe.
            clicks++;
        });
        container = new Container(button);
        root.Route(container, Bounds, PressAt(110, 210));
        root.DispatchCaptured(
            new UiPointerEvent(UiPointerEventKind.Released, new PointF(110, 210), PointerButton.Primary), Bounds);

        Assert.AreEqual(1, clicks);
        Assert.HasCount(2, button.Events);
    }

    [TestMethod]
    public void ButtonReleaseOutsideDoesNotClickAndWheelDoesNotRepaint()
    {
        int clicks = 0;
        var button = new ButtonInteraction(() => clicks++);
        var size = new SizeF(80, 40);
        Assert.IsTrue(button.HandlePointer(PressAt(10, 10), size).CapturePointer);
        button.HandlePointer(new UiPointerEvent(UiPointerEventKind.Released,
            new PointF(100, 10), PointerButton.Primary), size);
        Assert.AreEqual(0, clicks);

        UiPointerResult result = button.HandlePointer(
            new UiPointerEvent(UiPointerEventKind.Wheel, new PointF(10, 10), WheelDelta: 120), size);
        Assert.IsTrue(result.Consumed);
        Assert.IsFalse(result.NeedsRepaint);
    }

    private static UiPointerEvent PressAt(float x, float y)
    {
        return new UiPointerEvent(UiPointerEventKind.Pressed, new PointF(x, y), PointerButton.Primary);
    }

    private sealed class Element(Func<UiPointerEvent, UiPointerResult> handle) : IUiElement
    {
        internal List<UiPointerEvent> Events { get; } = [];

        public UiPointerResult HandlePointer(in UiPointerEvent input, SizeF size)
        {
            Events.Add(input);
            return handle(input);
        }

        public void Draw(in UiDrawContext context, SizeF size) { }
    }

    private sealed class ButtonElement(Action clicked) : IUiElement
    {
        private readonly ButtonInteraction _interaction = new(clicked);
        internal List<UiPointerEvent> Events { get; } = [];

        public UiPointerResult HandlePointer(in UiPointerEvent input, SizeF size)
        {
            Events.Add(input);
            return _interaction.HandlePointer(input, size);
        }

        public void Draw(in UiDrawContext context, SizeF size) { }
    }

    private sealed class Container(IUiElement child) : IUiElement
    {
        internal UiPointerRouter Router { get; } = new();

        public UiPointerResult HandlePointer(in UiPointerEvent input, SizeF size)
        {
            if (input.Kind == UiPointerEventKind.Cancelled)
            {
                return Router.Cancel();
            }

            var bounds = new RectangleF(PointF.Empty, size);
            return Router.Captured is not null
                ? Router.DispatchCaptured(input, bounds)
                : Router.Route(child, bounds, input);
        }

        public void Draw(in UiDrawContext context, SizeF size) { }
    }
}
