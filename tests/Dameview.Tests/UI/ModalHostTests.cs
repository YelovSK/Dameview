using Dameview.Platform;
using System.Drawing;
using Dameview.UI;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class ModalHostTests
{
    private static readonly SizeF WindowSize = new(800, 600);

    [TestMethod]
    public void EscapeClosesAndRestoresFocusExactlyOnce()
    {
        var host = new ModalHost(UiDpi.Default);
        var content = new Content();
        int restored = 0;
        host.Show(content, () => restored++);
        Assert.AreEqual(1, content.FocusCount);
        Assert.IsTrue(host.HandleKey(new UiKeyEvent(UiKey.Escape)));
        Assert.IsFalse(host.IsOpen);
        Assert.AreEqual(1, restored);
        host.Close();
        Assert.AreEqual(1, restored);
        Assert.IsFalse(host.HandleKey(new UiKeyEvent(UiKey.Right)));
    }

    [TestMethod]
    public void BackdropDismissalConsumesBothPressAndRelease()
    {
        var host = new ModalHost(UiDpi.Default);
        var content = new Content();
        host.Show(content, () => { });
        Assert.IsTrue(host.HandlePointer(Pointer(UiPointerEventKind.Pressed, 5, 5), WindowSize).Consumed);
        Assert.IsTrue(host.IsOpen);
        Assert.IsTrue(host.HandlePointer(Pointer(UiPointerEventKind.Released, 5, 5), WindowSize).Consumed);
        Assert.IsFalse(host.IsOpen);
        Assert.IsFalse(content.Events.Any(input => input.Kind == UiPointerEventKind.Pressed));
    }

    [TestMethod]
    public void CapturedReleaseOutsideContentDoesNotDismissModal()
    {
        var host = new ModalHost(UiDpi.Default);
        var content = new Content();
        host.Show(content, () => { });
        host.HandlePointer(Pointer(UiPointerEventKind.Pressed, 400, 300), WindowSize);
        host.HandlePointer(Pointer(UiPointerEventKind.Released, 5, 5), WindowSize);
        Assert.IsTrue(host.IsOpen);
        Assert.AreEqual(UiPointerEventKind.Released, content.Events[^1].Kind);
    }

    [TestMethod]
    public void ReplacementCancelsOldInteractionAndFocusesNewContent()
    {
        var host = new ModalHost(UiDpi.Default);
        var previous = new Content();
        var next = new Content();
        host.Show(previous, () => { });
        host.HandlePointer(Pointer(UiPointerEventKind.Pressed, 400, 300), WindowSize);
        host.Show(next, () => { });
        Assert.AreEqual(UiPointerEventKind.Cancelled, previous.Events[^1].Kind);
        Assert.AreEqual(1, next.FocusCount);
        host.HandlePointer(Pointer(UiPointerEventKind.Released, 5, 5), WindowSize);
        Assert.IsTrue(host.IsOpen);
        Assert.HasCount(0, next.Events);
    }

    [TestMethod]
    public void ContentCanDisableEscapeAndBackdropDismissal()
    {
        var host = new ModalHost(UiDpi.Default);
        var content = new Content { DismissOnBackdrop = false, DismissOnEscape = false };
        host.Show(content, () => { });
        Assert.IsTrue(host.HandleKey(new UiKeyEvent(UiKey.Escape)));
        host.HandlePointer(Pointer(UiPointerEventKind.Pressed, 5, 5), WindowSize);
        host.HandlePointer(Pointer(UiPointerEventKind.Released, 5, 5), WindowSize);
        Assert.IsTrue(host.IsOpen);
        Assert.AreEqual(UiKey.Escape, content.Keys.Single().Key);
    }

    [TestMethod]
    public void HitTestingUsesWindowDpiBeforeDrawingAndAfterDpiChanges()
    {
        var host = new ModalHost(144);
        var content = new Content();
        host.Show(content, () => { });
        host.HandlePointer(Pointer(UiPointerEventKind.Pressed, 120, 100), WindowSize);
        Assert.AreEqual(new PointF(20, 25), content.Events[^1].Position);
        host.HandlePointer(Pointer(UiPointerEventKind.Cancelled, 0, 0), WindowSize);

        host.SetDpi(192);
        host.HandlePointer(Pointer(UiPointerEventKind.Pressed, 40, 40), WindowSize);
        Assert.AreEqual(new PointF(16, 16), content.Events[^1].Position);
    }

    private static UiPointerEvent Pointer(UiPointerEventKind kind, float x, float y)
    {
        return new UiPointerEvent(kind, new PointF(x, y), PointerButton.Primary);
    }

    private sealed class Content : IModalContent
    {
        public SizeF PreferredSize => new(400, 300);
        public bool DismissOnBackdrop { get; init; } = true;
        public bool DismissOnEscape { get; init; } = true;
        internal int FocusCount { get; private set; }
        internal List<UiPointerEvent> Events { get; } = [];
        internal List<UiKeyEvent> Keys { get; } = [];

        public void Focus() => FocusCount++;
        public void HandleKey(UiKeyEvent input) => Keys.Add(input);
        public RectangleF GetFocusBounds(SizeF size) => new(20, 20, 100, 30);
        public void Draw(in UiDrawContext context, SizeF size) { }

        public UiPointerResult HandlePointer(in UiPointerEvent input, SizeF size)
        {
            Events.Add(input);
            return new UiPointerResult(Consumed: true, CapturePointer: input.Kind == UiPointerEventKind.Pressed);
        }
    }
}
