using System.Drawing;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.Win32.Input;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class InteractiveControlsTests
{
    [TestMethod]
    public void PointerActivationOccursOnPressOnly()
    {
        int clicks = 0;
        var button = new Button("Test", () => clicks++);
        var root = new UiRoot(button, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(100.0f, 36.0f));
        PointF center = new(50.0f, 18.0f);

        root.HandlePointer(Pointer(WindowPointerEventKind.Pressed, center));
        Assert.AreEqual(1, clicks);

        root.HandlePointer(Pointer(WindowPointerEventKind.Released, center));
        Assert.AreEqual(1, clicks);
    }

    [TestMethod]
    public void ToggleSharesKeyboardActivationAndDisabledBehavior()
    {
        bool? changed = null;
        var toggle = new Toggle("Gallery", false, value => changed = value);

        Assert.IsTrue(toggle.OnKeyEvent(new WindowKeyEvent(WindowKey.Space)));
        Assert.IsTrue(toggle.Value);
        Assert.AreEqual(true, changed);

        toggle.IsEnabled = false;
        Assert.IsFalse(toggle.OnKeyEvent(new WindowKeyEvent(WindowKey.Enter)));
        Assert.IsTrue(toggle.Value);
    }

    [TestMethod]
    public void TabArrowKeysSelectAndFocusTheAdjacentTab()
    {
        int selected = -1;
        var tabs = new TabStrip(["General", "Gallery"], 0, index => selected = index);
        var root = new UiRoot(tabs, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(300.0f, 36.0f));
        UiElement first = tabs.Children[0].Children[0];
        UiElement second = tabs.Children[0].Children[1];
        root.SetFocus(first);

        Assert.IsTrue(root.HandleKey(
            new WindowKeyEvent(WindowKey.Right),
            tabs,
            wrapFocus: true,
            directionalNavigation: true));

        Assert.AreEqual(1, tabs.SelectedIndex);
        Assert.AreEqual(1, selected);
        Assert.AreSame(second, root.FocusedElement);
    }

    [TestMethod]
    public void DropdownPopupEscapesItsParentAndSelectionReturnsFocusToTheControl()
    {
        var popupHost = new PopupHost();
        string? changed = null;
        var dropdown = new Dropdown<string>(
            popupHost,
            [new("Name", "name"), new("Modified", "modified")],
            "name",
            value => changed = value);
        var scene = new TestScene(dropdown, popupHost);
        var root = new UiRoot(scene, UiDpi.Default, TestTextLayouts.Shared);
        var size = new SizeF(600.0f, 400.0f);
        root.Arrange(size);
        root.SetFocus(dropdown);

        Assert.IsTrue(root.HandleKey(
            new WindowKeyEvent(WindowKey.Enter),
            scene,
            wrapFocus: true,
            directionalNavigation: true));
        root.Arrange(size);
        Assert.IsTrue(dropdown.IsOpen);
        Assert.IsTrue(popupHost.IsOpen);

        UiElement presenter = popupHost.Children[0];
        float collapsedHeight = presenter.Bounds.Height;
        AdvanceAnimation(root, size);
        Assert.IsTrue(presenter.Bounds.Height > collapsedHeight);
        // Found by type, so restructuring the presenter's nesting does not break this.
        UiElement secondOption = Descendants(presenter).OfType<Button>().ElementAt(1);
        RectangleF optionBounds = secondOption.GetBoundsRelativeTo(scene);
        PointF center = new(optionBounds.Left + optionBounds.Width / 2.0f, optionBounds.Top + optionBounds.Height / 2.0f);
        root.HandlePointer(Pointer(WindowPointerEventKind.Pressed, center));
        root.HandlePointer(Pointer(WindowPointerEventKind.Released, center));

        Assert.AreEqual("modified", dropdown.SelectedValue);
        Assert.AreEqual("modified", changed);
        Assert.IsFalse(popupHost.IsOpen);
        Assert.AreSame(dropdown, root.FocusedElement);
        Assert.IsTrue(presenter.IsVisible);
        Assert.AreSame(dropdown, scene.HitTest(new PointF(30.0f, 30.0f)));
        AdvanceAnimation(root, size);
        Assert.IsFalse(presenter.IsVisible);
        Assert.IsFalse(popupHost.IsVisible);
        Assert.IsEmpty(presenter.Children, "A closed popup lets go of its content.");
    }

    [TestMethod]
    public void DoubleClickingDropdownClosesItDuringOpeningAnimation()
    {
        var popupHost = new PopupHost();
        var dropdown = new Dropdown<int>(
            popupHost,
            [new("One", 1), new("Two", 2)],
            1,
            _ => { });
        var scene = new TestScene(dropdown, popupHost);
        var root = new UiRoot(scene, UiDpi.Default, TestTextLayouts.Shared);
        var size = new SizeF(600.0f, 400.0f);
        root.Arrange(size);

        PointF center = new(110.0f, 38.0f);
        root.HandlePointer(Pointer(WindowPointerEventKind.Pressed, center));
        root.HandlePointer(Pointer(WindowPointerEventKind.Released, center));
        root.Arrange(size);
        Assert.IsTrue(popupHost.IsOpen);

        root.HandlePointer(Pointer(WindowPointerEventKind.DoubleClicked, center));

        Assert.IsFalse(popupHost.IsOpen);
    }

    [TestMethod]
    public void ClickingOutsideDropdownDismissesWithoutClickThrough()
    {
        var popupHost = new PopupHost();
        var dropdown = new Dropdown<int>(
            popupHost,
            [new("One", 1), new("Two", 2)],
            1,
            _ => { });
        var scene = new TestScene(dropdown, popupHost);
        var root = new UiRoot(scene, UiDpi.Default, TestTextLayouts.Shared);
        var size = new SizeF(600.0f, 400.0f);
        root.Arrange(size);
        root.SetFocus(dropdown);
        root.HandleKey(new WindowKeyEvent(WindowKey.Enter), scene, wrapFocus: true, directionalNavigation: true);
        root.Arrange(size);

        Assert.IsTrue(root.HandleKey(
            new WindowKeyEvent(WindowKey.Escape),
            scene,
            wrapFocus: true,
            directionalNavigation: true));
        Assert.IsFalse(dropdown.IsOpen);
        root.HandleKey(new WindowKeyEvent(WindowKey.Enter), scene, wrapFocus: true, directionalNavigation: true);
        root.Arrange(size);

        bool pressed = root.HandlePointer(Pointer(WindowPointerEventKind.Pressed, new PointF(500.0f, 350.0f)));
        bool released = root.HandlePointer(Pointer(WindowPointerEventKind.Released, new PointF(500.0f, 350.0f)));

        Assert.IsTrue(pressed);
        Assert.IsTrue(released);
        Assert.IsFalse(dropdown.IsOpen);
    }

    private static WindowPointerEvent Pointer(WindowPointerEventKind kind, PointF position)
    {
        return new WindowPointerEvent(kind, position, PointerButton.Primary);
    }

    private static void AdvanceAnimation(UiRoot root, SizeF size)
    {
        for (int frame = 0; frame < 30; frame++)
        {
            root.Update(new UiUpdateContext(1.0 / 60.0));
            root.Arrange(size);
        }
    }

    private sealed class TestScene : UiElement
    {
        private readonly ControlContainer _controlContainer;
        private readonly PopupHost _popupHost;

        internal TestScene(UiElement control, PopupHost popupHost)
        {
            _controlContainer = new ControlContainer(control);
            _popupHost = popupHost;
            AddChild(_controlContainer);
            AddChild(popupHost);
        }

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            _controlContainer.Measure(new SizeF(180.0f, 36.0f));
            _popupHost.Measure(availableSize);
            return availableSize;
        }

        protected override void ArrangeCore(SizeF finalSize)
        {
            _controlContainer.Arrange(new RectangleF(20.0f, 20.0f, 180.0f, 36.0f));
            _popupHost.Arrange(new RectangleF(PointF.Empty, finalSize));
        }
    }

    private sealed class ControlContainer : UiElement
    {
        private readonly UiElement _control;

        internal ControlContainer(UiElement control)
        {
            _control = control;
            AddChild(control);
        }

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            _control.Measure(availableSize);
            return availableSize;
        }

        protected override void ArrangeCore(SizeF finalSize)
        {
            _control.Arrange(new RectangleF(PointF.Empty, finalSize));
        }
    }
    private static IEnumerable<UiElement> Descendants(UiElement element)
    {
        foreach (UiElement child in element.Children)
        {
            yield return child;
            foreach (UiElement descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

}
