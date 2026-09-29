using System.Drawing;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.Win32.Input;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class ContextMenuTests
{
    private static readonly SizeF WindowSize = new(800.0f, 600.0f);

    [TestMethod]
    public void OpensAtThePointWithinItsAnchorAndHoldsTheKeyboard()
    {
        (UiRoot root, Scene scene) = CreateScene();

        ContextMenu.Show(scene.Host, scene.Anchor, new PointF(50.0f, 60.0f), [[new ContextMenuItem("Copy", () => { })]]);
        Settle(root);

        RectangleF popup = scene.Host.Children[0].GetBoundsRelativeTo(scene);
        Assert.AreEqual(150.0f, popup.Left);
        Assert.IsTrue(popup.Top > 160.0f && popup.Top < 170.0f, $"Opened at {popup.Top}.");
        Assert.IsInstanceOfType<ContextMenu>(root.KeyboardCaptor);
    }

    [TestMethod]
    public void ArrowKeysSkipDisabledItemsAndEnterRunsTheItemOnceTheMenuHasClosed()
    {
        (UiRoot root, Scene scene) = CreateScene();
        string? invoked = null;
        bool openWhenInvoked = true;
        ContextMenuItem Item(string label, bool enabled = true) => new(label, () =>
        {
            invoked = label;
            openWhenInvoked = scene.Host.IsOpen;
        }, IsEnabled: enabled);
        ContextMenu.Show(scene.Host, scene.Anchor, PointF.Empty, [[Item("First"), Item("Second", enabled: false)], [Item("Third")]]);
        Settle(root);

        root.HandleCapturedKey(new WindowKeyEvent(WindowKey.Down));
        root.HandleCapturedKey(new WindowKeyEvent(WindowKey.Down));
        root.HandleCapturedKey(new WindowKeyEvent(WindowKey.Enter));

        Assert.AreEqual("Third", invoked);
        Assert.IsFalse(openWhenInvoked);
        Assert.IsNull(root.KeyboardCaptor);
    }

    [TestMethod]
    public void KeysThatMeanNothingToTheMenuGoNowhereElse()
    {
        (UiRoot root, Scene scene) = CreateScene();
        ContextMenu.Show(scene.Host, scene.Anchor, PointF.Empty, [[new ContextMenuItem("Copy", () => { })]]);
        Settle(root);

        Assert.IsTrue(root.HandleCapturedKey(new WindowKeyEvent(WindowKey.W, Control: true)));
        Assert.IsTrue(scene.Host.IsOpen);

        Assert.IsTrue(root.HandleCapturedKey(new WindowKeyEvent(WindowKey.Escape)));
        Assert.IsFalse(scene.Host.IsOpen);
        Assert.IsNull(root.KeyboardCaptor);
    }

    [TestMethod]
    public void ClickingAnItemRunsIt()
    {
        (UiRoot root, Scene scene) = CreateScene();
        int copies = 0;
        ContextMenu.Show(scene.Host, scene.Anchor, PointF.Empty, [[new ContextMenuItem("Copy", () => copies++)]]);
        Settle(root);

        RectangleF popup = scene.Host.Children[0].GetBoundsRelativeTo(scene);
        var item = new PointF(popup.Left + 20.0f, popup.Top + 20.0f);
        root.HandlePointer(new WindowPointerEvent(WindowPointerEventKind.Pressed, item, PointerButton.Primary));
        root.HandlePointer(new WindowPointerEvent(WindowPointerEventKind.Released, item, PointerButton.Primary));

        Assert.AreEqual(1, copies);
        Assert.IsFalse(scene.Host.IsOpen);
    }

    [TestMethod]
    public void RightClickingElsewhereClosesTheMenuWithoutReachingWhatIsBelow()
    {
        (UiRoot root, Scene scene) = CreateScene();
        ContextMenu.Show(scene.Host, scene.Anchor, PointF.Empty, [[new ContextMenuItem("Copy", () => { })]]);
        Settle(root);

        var elsewhere = new PointF(290.0f, 190.0f);
        root.HandlePointer(new WindowPointerEvent(WindowPointerEventKind.Pressed, elsewhere, PointerButton.Secondary));
        root.HandlePointer(new WindowPointerEvent(WindowPointerEventKind.Released, elsewhere, PointerButton.Secondary));

        Assert.IsFalse(scene.Host.IsOpen);
        Assert.IsNull(root.KeyboardCaptor);
        Assert.AreEqual(0, scene.Anchor.Events);
    }

    [TestMethod]
    public void ScrollingElsewhereClosesTheMenuWithoutScrollingWhatIsBelow()
    {
        (UiRoot root, Scene scene) = CreateScene();
        ContextMenu.Show(scene.Host, scene.Anchor, PointF.Empty, [[new ContextMenuItem("Copy", () => { })]]);
        Settle(root);

        root.HandlePointer(new WindowPointerEvent(
            WindowPointerEventKind.Wheel, new PointF(290.0f, 190.0f), WheelDelta: -120));

        Assert.IsFalse(scene.Host.IsOpen);
        Assert.AreEqual(0, scene.Anchor.Events);
    }

    [TestMethod]
    public void AMenuWithNothingInItDoesNotOpen()
    {
        (UiRoot root, Scene scene) = CreateScene();

        ContextMenu.Show(scene.Host, scene.Anchor, PointF.Empty, [[], []]);

        Assert.IsFalse(scene.Host.IsOpen);
        Assert.IsNull(root.KeyboardCaptor);
    }

    private static (UiRoot Root, Scene Scene) CreateScene()
    {
        var scene = new Scene();
        var root = new UiRoot(scene, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(WindowSize);
        return (root, scene);
    }

    private static void Settle(UiRoot root)
    {
        for (int frame = 0; frame < 30; frame++)
        {
            root.Update(new UiUpdateContext(1.0 / 60.0));
            root.Arrange(WindowSize);
        }
    }

    private sealed class Scene : UiElement
    {
        internal Scene()
        {
            AddChild(Anchor);
            AddChild(Host);
        }

        internal RecordingElement Anchor { get; } = new();
        internal PopupHost Host { get; } = new();

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            Host.Measure(availableSize);
            return availableSize;
        }

        protected override void ArrangeCore(SizeF finalSize)
        {
            Anchor.Arrange(new RectangleF(100.0f, 100.0f, 200.0f, 100.0f));
            Host.Arrange(new RectangleF(PointF.Empty, finalSize));
        }
    }

    private sealed class RecordingElement : UiElement
    {
        internal int Events { get; private set; }

        internal override UiPointerResult OnPointerEvent(in WindowPointerEvent input)
        {
            Events++;
            return new UiPointerResult(Consumed: true);
        }
    }
}
