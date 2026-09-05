using System.Drawing;
using Dameview.Platform;
using Dameview.UI;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class ModalHostTests
{
    private static readonly SizeF WindowSize = new(800, 600);

    [TestMethod]
    public void EscapeClosesAndFocusCanBeRestoredExactlyOnce()
    {
        int dismissed = 0;
        ModalHost? host = null;
        host = new ModalHost(() =>
        {
            dismissed++;
            host!.Close();
        });
        var content = new Content();
        var root = CreateRoot(host);
        host.Show(content);
        root.SetFocus(content.InitialFocus);

        Assert.IsTrue(host.HandleEscape());
        Assert.IsFalse(host.IsOpen);
        Assert.AreEqual(1, dismissed);
        host.Close();
        Assert.AreEqual(1, dismissed);
        Assert.IsFalse(host.HandleEscape());
    }

    [TestMethod]
    public void BackdropDismissalConsumesBothPressAndRelease()
    {
        ModalHost? host = null;
        host = new ModalHost(() => host!.Close());
        var content = new Content();
        var root = CreateRoot(host);
        host.Show(content);
        root.Arrange(WindowSize);

        Assert.IsTrue(root.HandlePointer(Pointer(UiPointerEventKind.Pressed, 5, 5)));
        Assert.IsTrue(host.IsOpen);
        Assert.IsTrue(root.HandlePointer(Pointer(UiPointerEventKind.Released, 5, 5)));
        Assert.IsFalse(host.IsOpen);
        Assert.IsFalse(content.Events.Any(input => input.Kind == UiPointerEventKind.Pressed));
    }

    [TestMethod]
    public void CapturedReleaseOutsideContentDoesNotDismissModal()
    {
        ModalHost? host = null;
        host = new ModalHost(() => host!.Close());
        var content = new Content();
        var root = CreateRoot(host);
        host.Show(content);
        root.Arrange(WindowSize);

        root.HandlePointer(Pointer(UiPointerEventKind.Pressed, 400, 300));
        root.HandlePointer(Pointer(UiPointerEventKind.Released, 5, 5));

        Assert.IsTrue(host.IsOpen);
        Assert.AreEqual(UiPointerEventKind.Released, content.Events[^1].Kind);
    }

    [TestMethod]
    public void ReplacementCancelsOldInteractionBeforeInstallingNewContent()
    {
        var host = new ModalHost(() => { });
        var previous = new Content();
        var next = new Content();
        var root = CreateRoot(host);
        host.Show(previous);
        root.Arrange(WindowSize);
        root.HandlePointer(Pointer(UiPointerEventKind.Pressed, 400, 300));

        host.Show(next);
        root.Arrange(WindowSize);
        root.HandlePointer(Pointer(UiPointerEventKind.Released, 5, 5));

        Assert.AreEqual(UiPointerEventKind.Cancelled, previous.Events[^1].Kind);
        Assert.IsTrue(host.IsOpen);
        Assert.HasCount(0, next.Events);
    }

    [TestMethod]
    public void ContentCanDisableEscapeAndBackdropDismissal()
    {
        var host = new ModalHost(() => Assert.Fail("Modal should not be dismissed."));
        var content = new Content
        {
            CanDismissOnBackdrop = false,
            CanDismissOnEscape = false,
        };
        var root = CreateRoot(host);
        host.Show(content);
        root.Arrange(WindowSize);

        Assert.IsTrue(host.HandleEscape());
        root.HandlePointer(Pointer(UiPointerEventKind.Pressed, 5, 5));
        root.HandlePointer(Pointer(UiPointerEventKind.Released, 5, 5));

        Assert.IsTrue(host.IsOpen);
        Assert.AreEqual(UiKey.Escape, content.Keys.Single().Key);
    }

    [TestMethod]
    public void HitTestingUsesRootDpiBeforeDrawingAndAfterDpiChanges()
    {
        var host = new ModalHost(() => { });
        var content = new Content();
        var root = CreateRoot(host, 144);
        host.Show(content);
        root.Arrange(WindowSize);

        root.HandlePointer(Pointer(UiPointerEventKind.Pressed, 120, 100));
        AssertPoint(new PointF(13.333336f, 16.666668f), content.Events[^1].Position);
        root.HandlePointer(new UiPointerEvent(UiPointerEventKind.Cancelled, PointF.Empty));

        root.SetDpi(192);
        root.Arrange(WindowSize);
        root.HandlePointer(Pointer(UiPointerEventKind.Pressed, 40, 40));
        AssertPoint(new PointF(8, 8), content.Events[^1].Position);
    }

    [TestMethod]
    public void ClickingEmptyModalContentPreservesItsFocusedChild()
    {
        var host = new ModalHost(() => { });
        var content = new ContentWithChild(40);
        var root = CreateRoot(host);
        host.Show(content);
        root.Arrange(WindowSize);
        root.SetFocus(content.InitialFocus);

        root.HandlePointer(Pointer(UiPointerEventKind.Pressed, 400, 300));

        Assert.AreSame(content.InitialFocus, root.FocusedElement);
    }

    [TestMethod]
    public void FocusingOffscreenModalContentScrollsItIntoView()
    {
        var host = new ModalHost(() => { });
        var content = new ContentWithChild(650, preferredHeight: 700);
        var root = CreateRoot(host);
        host.Show(content);
        root.Arrange(WindowSize);

        root.SetFocus(content.InitialFocus);

        RectangleF focusedBounds = content.InitialFocus.GetBoundsRelativeTo(host);
        Assert.IsGreaterThanOrEqualTo(12.0f, focusedBounds.Top);
        Assert.IsLessThanOrEqualTo(WindowSize.Height - 12.0f, focusedBounds.Bottom);
    }

    private static UiRoot CreateRoot(ModalHost host, float dpi = UiDpi.Default)
    {
        return new UiRoot(new RootElement(host), dpi);
    }

    private static UiPointerEvent Pointer(UiPointerEventKind kind, float x, float y)
    {
        return new UiPointerEvent(kind, new PointF(x, y), PointerButton.Primary);
    }

    private static void AssertPoint(PointF expected, PointF actual)
    {
        Assert.AreEqual(expected.X, actual.X, 0.001f);
        Assert.AreEqual(expected.Y, actual.Y, 0.001f);
    }

    private sealed class RootElement : UiElement
    {
        private readonly ModalHost _host;

        internal RootElement(ModalHost host)
        {
            _host = host;
            AddChild(host);
        }

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            _host.Measure(availableSize);
            return availableSize;
        }

        protected override void ArrangeCore(SizeF finalSize)
        {
            _host.Arrange(new RectangleF(PointF.Empty, finalSize));
        }
    }

    private sealed class Content : ModalContent
    {
        internal override SizeF PreferredSize => new(400, 300);
        internal override UiElement InitialFocus => this;
        internal override bool DismissOnBackdrop => CanDismissOnBackdrop;
        internal override bool DismissOnEscape => CanDismissOnEscape;
        internal override bool IsFocusable => true;
        internal bool CanDismissOnBackdrop { get; init; } = true;
        internal bool CanDismissOnEscape { get; init; } = true;
        internal List<UiPointerEvent> Events { get; } = [];
        internal List<UiKeyEvent> Keys { get; } = [];

        internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
        {
            Events.Add(input);
            return new UiPointerResult(
                Consumed: true,
                CapturePointer: input.Kind == UiPointerEventKind.Pressed);
        }

        internal override bool OnKeyEvent(UiKeyEvent input)
        {
            Keys.Add(input);
            return true;
        }
    }

    private sealed class ContentWithChild : ModalContent
    {
        private readonly UiElement _focus = new FocusTarget();
        private readonly float _focusY;
        private readonly float _preferredHeight;

        internal ContentWithChild(float focusY, float preferredHeight = 300)
        {
            _focusY = focusY;
            _preferredHeight = preferredHeight;
            AddChild(_focus);
        }

        internal override SizeF PreferredSize => new(400, _preferredHeight);
        internal override UiElement InitialFocus => _focus;

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            _focus.Measure(new SizeF(100, 30));
            return PreferredSize;
        }

        protected override void ArrangeCore(SizeF finalSize)
        {
            _focus.Arrange(new RectangleF(20, _focusY, 100, 30));
        }
    }

    private sealed class FocusTarget : UiElement
    {
        internal override bool IsFocusable => true;
    }
}
