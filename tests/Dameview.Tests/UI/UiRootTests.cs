using System.Drawing;
using Dameview.Platform;
using Dameview.UI;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class UiRootTests
{
    [TestMethod]
    public void ArrangedBoundsDriveHitTestingAndDpiConversion()
    {
        var child = new TestElement(_ => new UiPointerResult(Consumed: true));
        var content = new TestContainer(child, new RectangleF(100, 200, 80, 40));
        var root = new UiRoot(content, 144);
        root.Arrange(new SizeF(900, 600));

        root.HandlePointer(Pointer(UiPointerEventKind.Pressed, 165.0f, 315.0f));

        Assert.HasCount(1, child.Events);
        Assert.AreEqual(new PointF(10, 10), child.Events[0].Position);
    }

    [TestMethod]
    public void CapturedReleaseUsesCurrentBoundsAndClearsCaptureBeforeCallback()
    {
        UiRoot? root = null;
        var child = new TestElement(input =>
        {
            if (input.Kind == UiPointerEventKind.Released)
            {
                Assert.IsNull(root!.CapturedElement);
            }

            return new UiPointerResult(
                Consumed: true,
                CapturePointer: input.Kind == UiPointerEventKind.Pressed);
        });
        var content = new TestContainer(child, new RectangleF(100, 200, 80, 40));
        root = new UiRoot(content, UiDpi.Default);
        root.Arrange(new SizeF(800, 600));
        root.HandlePointer(Pointer(UiPointerEventKind.Pressed, 110, 210));
        Assert.AreSame(child, root.CapturedElement);

        content.ChildBounds = new RectangleF(120, 230, 80, 40);
        root.InvalidateLayout();
        root.Arrange(new SizeF(800, 600));
        root.HandlePointer(Pointer(UiPointerEventKind.Released, 250, 300));

        Assert.AreEqual(new PointF(130, 70), child.Events[1].Position);
        Assert.IsNull(root.CapturedElement);
    }

    [TestMethod]
    public void CancellationClearsCaptureAndCannotActivateALateRelease()
    {
        int clicks = 0;
        var child = new CapturingElement(() => clicks++);
        var content = new TestContainer(child, new RectangleF(100, 200, 80, 40));
        var root = new UiRoot(content, UiDpi.Default);
        root.Arrange(new SizeF(800, 600));

        root.HandlePointer(Pointer(UiPointerEventKind.Pressed, 110, 210));
        root.HandlePointer(new UiPointerEvent(UiPointerEventKind.Cancelled, PointF.Empty));
        root.HandlePointer(Pointer(UiPointerEventKind.Released, 110, 210));

        Assert.IsNull(root.CapturedElement);
        Assert.AreEqual(0, clicks);
        Assert.AreEqual(UiPointerEventKind.Cancelled, child.Events[1].Kind);
    }

    [TestMethod]
    public void RootOwnsHoverFocusAndPressedVisualStates()
    {
        var child = new FocusableElement();
        var content = new TestContainer(child, new RectangleF(100, 200, 80, 40));
        var root = new UiRoot(content, UiDpi.Default);
        root.Arrange(new SizeF(800, 600));

        root.HandlePointer(Pointer(UiPointerEventKind.Moved, 110, 210));
        Assert.IsTrue(child.HasVisualState(UiVisualState.Hovered));

        root.HandlePointer(Pointer(UiPointerEventKind.Pressed, 110, 210));
        Assert.AreSame(child, root.FocusedElement);
        Assert.IsTrue(child.HasVisualState(UiVisualState.Focused));
        Assert.IsTrue(child.HasVisualState(UiVisualState.Pressed));

        root.HandlePointer(Pointer(UiPointerEventKind.Released, 300, 300));
        Assert.IsFalse(child.HasVisualState(UiVisualState.Pressed));
        Assert.IsFalse(child.HasVisualState(UiVisualState.Hovered));
    }

    [TestMethod]
    public void VisualInvalidationDoesNotRepeatLayoutButLayoutInvalidationDoes()
    {
        var content = new TestContainer(new TestElement(), new RectangleF(0, 0, 10, 10));
        var root = new UiRoot(content, UiDpi.Default);
        var size = new SizeF(800, 600);
        root.Arrange(size);
        Assert.AreEqual(1, content.ArrangeCount);

        root.InvalidateVisual();
        root.Arrange(size);
        Assert.AreEqual(1, content.ArrangeCount);

        root.InvalidateLayout();
        root.Arrange(size);
        Assert.AreEqual(2, content.ArrangeCount);
    }

    [TestMethod]
    public void UnconsumedPointerEventsBubbleToTheParent()
    {
        var child = new TestElement();
        var content = new TestContainer(child, new RectangleF(100, 200, 80, 40))
        {
            ConsumeWheel = true,
        };
        var root = new UiRoot(content, UiDpi.Default);
        root.Arrange(new SizeF(800, 600));

        bool consumed = root.HandlePointer(new UiPointerEvent(
            UiPointerEventKind.Wheel,
            new PointF(110, 210),
            WheelDelta: 120));

        Assert.IsTrue(consumed);
        Assert.HasCount(1, child.Events);
        Assert.AreEqual(1, content.WheelCount);
    }

    [TestMethod]
    public void KeyboardNavigationUsesVisibleFocusableDescendantsInTreeOrder()
    {
        var first = new FocusableElement();
        var second = new FocusableElement();
        var content = new FocusContainer(first, second);
        var root = new UiRoot(content, UiDpi.Default);
        root.Arrange(new SizeF(800, 600));

        Assert.IsTrue(root.HandleKey(new UiKeyEvent(UiKey.Tab), content, wrapFocus: true,
            directionalNavigation: true));
        Assert.AreSame(first, root.FocusedElement);
        root.HandleKey(new UiKeyEvent(UiKey.Right), content, wrapFocus: true,
            directionalNavigation: true);
        Assert.AreSame(second, root.FocusedElement);
        root.HandleKey(new UiKeyEvent(UiKey.Tab), content, wrapFocus: true,
            directionalNavigation: true);
        Assert.AreSame(first, root.FocusedElement);
    }

    [TestMethod]
    public void HidingASubtreeClearsItsCaptureHoverAndFocus()
    {
        var child = new FocusableElement();
        var content = new TestContainer(child, new RectangleF(100, 200, 80, 40));
        var root = new UiRoot(content, UiDpi.Default);
        root.Arrange(new SizeF(800, 600));
        root.HandlePointer(Pointer(UiPointerEventKind.Moved, 110, 210));
        root.HandlePointer(Pointer(UiPointerEventKind.Pressed, 110, 210));

        child.IsVisible = false;

        Assert.IsNull(root.CapturedElement);
        Assert.IsNull(root.FocusedElement);
        Assert.IsFalse(child.HasVisualState(UiVisualState.Hovered));
        Assert.IsFalse(child.HasVisualState(UiVisualState.Pressed));
        Assert.IsFalse(child.HasVisualState(UiVisualState.Focused));
    }

    private static UiPointerEvent Pointer(UiPointerEventKind kind, float x, float y)
    {
        return new UiPointerEvent(kind, new PointF(x, y), PointerButton.Primary);
    }

    private class TestElement(Func<UiPointerEvent, UiPointerResult>? handle = null) : UiElement
    {
        internal List<UiPointerEvent> Events { get; } = [];

        internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
        {
            Events.Add(input);
            return handle?.Invoke(input) ?? default;
        }
    }

    private sealed class FocusableElement : TestElement
    {
        internal override bool IsFocusable => true;

        internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
        {
            base.OnPointerEvent(input);
            return new UiPointerResult(
                Consumed: true,
                CapturePointer: input.Kind == UiPointerEventKind.Pressed);
        }
    }

    private sealed class CapturingElement(Action clicked) : TestElement
    {
        private bool _pressed;

        internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
        {
            base.OnPointerEvent(input);
            switch (input.Kind)
            {
                case UiPointerEventKind.Pressed:
                    _pressed = true;
                    return new UiPointerResult(Consumed: true, CapturePointer: true);
                case UiPointerEventKind.Released when _pressed:
                    _pressed = false;
                    clicked();
                    return new UiPointerResult(Consumed: true);
                case UiPointerEventKind.Cancelled:
                    _pressed = false;
                    return new UiPointerResult(Consumed: true);
                default:
                    return default;
            }
        }
    }

    private sealed class TestContainer : UiElement
    {
        private readonly UiElement _child;

        internal TestContainer(UiElement child, RectangleF childBounds)
        {
            _child = child;
            ChildBounds = childBounds;
            AddChild(child);
        }

        internal RectangleF ChildBounds { get; set; }
        internal int ArrangeCount { get; private set; }
        internal bool ConsumeWheel { get; init; }
        internal int WheelCount { get; private set; }

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            _child.Measure(ChildBounds.Size);
            return availableSize;
        }

        protected override void ArrangeCore(SizeF finalSize)
        {
            ArrangeCount++;
            _child.Arrange(ChildBounds);
        }

        internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
        {
            if (input.Kind == UiPointerEventKind.Wheel)
            {
                WheelCount++;
                return new UiPointerResult(Consumed: ConsumeWheel);
            }

            return default;
        }
    }

    private sealed class FocusContainer : UiElement
    {
        private readonly UiElement[] _children;

        internal FocusContainer(params UiElement[] children)
        {
            _children = children;
            foreach (UiElement child in children)
            {
                AddChild(child);
            }
        }

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            foreach (UiElement child in _children)
            {
                child.Measure(new SizeF(100, 30));
            }

            return availableSize;
        }

        protected override void ArrangeCore(SizeF finalSize)
        {
            for (int index = 0; index < _children.Length; index++)
            {
                _children[index].Arrange(new RectangleF(0, index * 40, 100, 30));
            }
        }
    }
}
