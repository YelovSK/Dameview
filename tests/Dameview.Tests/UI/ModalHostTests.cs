using System.Drawing;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.Win32.Input;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class ModalHostTests
{
    private static readonly SizeF WindowSize = new(800, 600);

    [TestMethod]
    public void EscapeDismissesOnceAndClosingAgainIsInert()
    {
        int dismissed = 0;
        var host = new ModalHost();
        var content = new Content();
        _ = CreateRoot(host);
        host.Show(content, () =>
        {
            dismissed++;
            host.Close();
        });

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
        var host = new ModalHost();
        var content = new Content();
        UiRoot root = CreateRoot(host);
        host.Show(content, host.Close);
        root.Arrange(WindowSize);

        Assert.IsTrue(root.HandlePointer(Pointer(WindowPointerEventKind.Pressed, 5, 5)));
        Assert.IsTrue(host.IsOpen);
        Assert.IsTrue(root.HandlePointer(Pointer(WindowPointerEventKind.Released, 5, 5)));
        Assert.IsFalse(host.IsOpen);
        Assert.IsFalse(content.Events.Any(input => input.Kind == WindowPointerEventKind.Pressed));
    }

    [TestMethod]
    public void CapturedReleaseOutsideContentDoesNotDismissModal()
    {
        var host = new ModalHost();
        var content = new Content();
        UiRoot root = CreateRoot(host);
        host.Show(content, host.Close);
        root.Arrange(WindowSize);
        FinishAnimations(root);

        root.HandlePointer(Pointer(WindowPointerEventKind.Pressed, 400, 300));
        root.HandlePointer(Pointer(WindowPointerEventKind.Released, 5, 5));

        Assert.IsTrue(host.IsOpen);
        Assert.AreEqual(WindowPointerEventKind.Released, content.Events[^1].Kind);
    }

    [TestMethod]
    public void ClosingKeepsTheContentOnScreenButInertUntilItHasFadedOut()
    {
        var host = new ModalHost();
        var content = new Content();
        UiRoot root = CreateRoot(host);
        host.Show(content, host.Close);
        root.Arrange(WindowSize);
        FinishAnimations(root);
        root.SetFocus(content.InitialFocus);

        host.Close();

        Assert.IsFalse(host.IsOpen);
        Assert.IsFalse(host.IsHitTestVisible);
        Assert.IsNull(root.FocusedElement);
        Assert.AreSame(host, content.Parent?.Parent);

        FinishAnimations(root);

        Assert.IsNull(content.Parent);
        Assert.IsFalse(host.IsVisible);
    }

    [TestMethod]
    public void ReplacementCancelsOldInteractionBeforeInstallingNewContent()
    {
        var host = new ModalHost();
        var previous = new Content();
        var next = new Content();
        UiRoot root = CreateRoot(host);
        host.Show(previous, () => { });
        root.Arrange(WindowSize);
        FinishAnimations(root);
        root.HandlePointer(Pointer(WindowPointerEventKind.Pressed, 400, 300));

        host.Show(next, () => { });
        root.Arrange(WindowSize);
        root.HandlePointer(Pointer(WindowPointerEventKind.Released, 5, 5));

        Assert.AreEqual(WindowPointerEventKind.Cancelled, previous.Events[^1].Kind);
        Assert.IsTrue(host.IsOpen);
        Assert.HasCount(0, next.Events);
    }

    [TestMethod]
    public void ReplacementUsesTheActiveContentsDismissCallback()
    {
        var host = new ModalHost();
        var previous = new Content();
        var next = new Content();
        int previousDismissals = 0;
        int nextDismissals = 0;

        host.Show(previous, () => previousDismissals++);
        host.Show(next, () => nextDismissals++);
        host.HandleEscape();

        Assert.AreSame(next, host.Content);
        Assert.AreEqual(0, previousDismissals);
        Assert.AreEqual(1, nextDismissals);
    }

    [TestMethod]
    public void ContentCanDisableEscapeAndBackdropDismissal()
    {
        var host = new ModalHost();
        var content = new Content
        {
            CanDismissOnBackdrop = false,
            CanDismissOnEscape = false,
        };
        UiRoot root = CreateRoot(host);
        host.Show(content, () => Assert.Fail("Modal should not be dismissed."));
        root.Arrange(WindowSize);

        Assert.IsTrue(host.HandleEscape());
        root.HandlePointer(Pointer(WindowPointerEventKind.Pressed, 5, 5));
        root.HandlePointer(Pointer(WindowPointerEventKind.Released, 5, 5));

        Assert.IsTrue(host.IsOpen);
        Assert.AreEqual(WindowKey.Escape, content.Keys.Single().Key);
    }

    [TestMethod]
    public void HitTestingUsesRootDpiBeforeDrawingAndAfterDpiChanges()
    {
        var host = new ModalHost();
        var content = new Content();
        UiRoot root = CreateRoot(host, 144);
        host.Show(content, () => { });
        root.Arrange(WindowSize);
        FinishAnimations(root);

        root.HandlePointer(Pointer(WindowPointerEventKind.Pressed, 120, 100));
        AssertPoint(ContentLocal(content, host, 144.0f, 120, 100), content.Events[^1].Position);
        root.HandlePointer(new WindowPointerEvent(WindowPointerEventKind.Cancelled, PointF.Empty));

        root.SetDpi(192);
        root.Arrange(WindowSize);
        root.HandlePointer(Pointer(WindowPointerEventKind.Pressed, 40, 40));
        AssertPoint(ContentLocal(content, host, 192.0f, 40, 40), content.Events[^1].Position);
    }

    [TestMethod]
    public void SurfaceEdgesAreAlignedToPhysicalPixels()
    {
        const float dpi = 120.0f;
        var host = new ModalHost();
        var content = new Content();
        UiRoot root = CreateRoot(host, dpi);
        host.Show(content, () => { });

        root.Arrange(WindowSize);

        RectangleF bounds = content.GetBoundsRelativeTo(host);
        AssertPixelAligned(bounds.Left, dpi);
        AssertPixelAligned(bounds.Top, dpi);
        AssertPixelAligned(bounds.Right, dpi);
        AssertPixelAligned(bounds.Bottom, dpi);
    }

    [TestMethod]
    public void ClickingEmptyModalContentPreservesItsFocusedChild()
    {
        var host = new ModalHost();
        var content = new ContentWithChild(40);
        UiRoot root = CreateRoot(host);
        host.Show(content, () => { });
        root.Arrange(WindowSize);
        root.SetFocus(content.InitialFocus);

        root.HandlePointer(Pointer(WindowPointerEventKind.Pressed, 400, 300));

        Assert.AreSame(content.InitialFocus, root.FocusedElement);
    }

    [TestMethod]
    public void FocusingOffscreenModalContentScrollsItIntoView()
    {
        var host = new ModalHost();
        var content = new ContentWithChild(650, preferredHeight: 700);
        UiRoot root = CreateRoot(host);
        host.Show(content, () => { });
        root.Arrange(WindowSize);

        root.SetFocus(content.InitialFocus);

        RectangleF focusedBounds = content.InitialFocus.GetBoundsRelativeTo(host);
        Assert.IsGreaterThanOrEqualTo(12.0f, focusedBounds.Top);
        Assert.IsLessThanOrEqualTo(WindowSize.Height - 12.0f, focusedBounds.Bottom);
    }

    private static UiRoot CreateRoot(ModalHost host, float dpi = UiDpi.Default)
    {
        return new UiRoot(new RootElement(host), dpi, TestTextLayouts.Shared);
    }

    // Content that is still fading in cannot be clicked yet.
    private static void FinishAnimations(UiRoot root) =>
        root.Update(new UiUpdateContext(0.0, AnimationsEnabled: false));

    private static WindowPointerEvent Pointer(WindowPointerEventKind kind, float x, float y)
    {
        return new WindowPointerEvent(kind, new PointF(x, y), PointerButton.Primary);
    }

    private static void AssertPoint(PointF expected, PointF actual)
    {
        Assert.AreEqual(expected.X, actual.X, 0.001f);
        Assert.AreEqual(expected.Y, actual.Y, 0.001f);
    }

    private static void AssertPixelAligned(float value, float dpi)
    {
        float pixels = UiDpi.DipsToPixels(value, dpi);
        Assert.AreEqual(MathF.Round(pixels), pixels, 0.001f);
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
        internal List<WindowPointerEvent> Events { get; } = [];
        internal List<WindowKeyEvent> Keys { get; } = [];

        internal override UiPointerResult OnPointerEvent(in WindowPointerEvent input)
        {
            Events.Add(input);
            return new UiPointerResult(
                Consumed: true,
                CapturePointer: input.Kind == WindowPointerEventKind.Pressed);
        }

        internal override bool OnKeyEvent(WindowKeyEvent input)
        {
            Keys.Add(input);
            return true;
        }
    }

    private sealed class ContentWithChild : ModalContent
    {
        private readonly UiElement _focus = new FocusTarget();
        private readonly ScrollView _scrollView;
        private readonly float _preferredHeight;

        internal ContentWithChild(float focusY, float preferredHeight = 300)
        {
            _preferredHeight = preferredHeight;
            _scrollView = new ScrollView(new ScrollBody(_focus, focusY, preferredHeight));
            AddChild(_scrollView);
        }

        internal override SizeF PreferredSize => new(400, _preferredHeight);
        internal override UiElement InitialFocus => _focus;

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            _scrollView.Measure(availableSize);
            return PreferredSize;
        }

        protected override void ArrangeCore(SizeF finalSize)
        {
            _scrollView.Arrange(new RectangleF(PointF.Empty, finalSize));
        }
    }

    private sealed class ScrollBody : UiElement
    {
        private readonly UiElement _focus;
        private readonly float _focusY;
        private readonly float _height;

        internal ScrollBody(UiElement focus, float focusY, float height)
        {
            _focus = focus;
            _focusY = focusY;
            _height = height;
            AddChild(focus);
        }

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            _focus.Measure(new SizeF(100.0f, 30.0f));
            return new SizeF(availableSize.Width, _height);
        }

        protected override void ArrangeCore(SizeF finalSize)
        {
            _focus.Arrange(new RectangleF(20.0f, _focusY, 100.0f, 30.0f));
        }

        protected override bool HitTestCore(PointF position) => false;
    }

    private sealed class FocusTarget : UiElement
    {
        internal override bool IsFocusable => true;
    }
    // The content sees the pointer converted to dips and made relative to where it was placed,
    // so the expectation is derived rather than a constant that hides the modal's margin.
    private static PointF ContentLocal(UiElement content, UiElement root, float dpi, float x, float y)
    {
        RectangleF bounds = content.GetBoundsRelativeTo(root);
        return new PointF((x * 96.0f / dpi) - bounds.X, (y * 96.0f / dpi) - bounds.Y);
    }

}
