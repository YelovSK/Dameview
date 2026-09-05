using System.Drawing;
using Dameview.Platform;
using Dameview.UI;
using Dameview.UI.Components;
using Vortice.DirectWrite;
using static Vortice.DirectWrite.DWrite;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class InteractiveControlsTests
{
    [TestMethod]
    public void ToggleSharesKeyboardActivationAndDisabledBehavior()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        bool? changed = null;
        using var toggle = new Toggle(factory, "Gallery", false, value => changed = value);

        Assert.IsTrue(toggle.OnKeyEvent(new UiKeyEvent(UiKey.Space)));
        Assert.IsTrue(toggle.Value);
        Assert.AreEqual(true, changed);

        toggle.IsEnabled = false;
        Assert.IsFalse(toggle.OnKeyEvent(new UiKeyEvent(UiKey.Enter)));
        Assert.IsTrue(toggle.Value);
    }

    [TestMethod]
    public void TabArrowKeysSelectAndFocusTheAdjacentTab()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        int selected = -1;
        using var tabs = new TabStrip(factory, ["General", "Gallery"], 0, index => selected = index);
        var root = new UiRoot(tabs, UiDpi.Default);
        root.Arrange(new SizeF(300.0f, 36.0f));
        UiElement first = tabs.Children[0].Children[0];
        UiElement second = tabs.Children[0].Children[1];
        root.SetFocus(first);

        Assert.IsTrue(root.HandleKey(
            new UiKeyEvent(UiKey.Right),
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
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        var popupHost = new PopupHost();
        string? changed = null;
        using var dropdown = new Dropdown<string>(
            factory,
            popupHost,
            [new("Name", "name"), new("Modified", "modified")],
            "name",
            value => changed = value);
        var scene = new TestScene(dropdown, popupHost);
        var root = new UiRoot(scene, UiDpi.Default);
        var size = new SizeF(600.0f, 400.0f);
        root.Arrange(size);
        root.SetFocus(dropdown);

        Assert.IsTrue(root.HandleKey(
            new UiKeyEvent(UiKey.Enter),
            scene,
            wrapFocus: true,
            directionalNavigation: true));
        root.Arrange(size);
        Assert.IsTrue(dropdown.IsOpen);
        Assert.IsTrue(popupHost.IsOpen);

        UiElement secondOption = popupHost.Children[0].Children[0].Children[1];
        RectangleF optionBounds = secondOption.GetBoundsRelativeTo(scene);
        PointF center = new(optionBounds.Left + optionBounds.Width / 2.0f, optionBounds.Top + optionBounds.Height / 2.0f);
        root.HandlePointer(Pointer(UiPointerEventKind.Pressed, center));
        root.HandlePointer(Pointer(UiPointerEventKind.Released, center));

        Assert.AreEqual("modified", dropdown.SelectedValue);
        Assert.AreEqual("modified", changed);
        Assert.IsFalse(popupHost.IsOpen);
        Assert.AreSame(dropdown, root.FocusedElement);
    }

    [TestMethod]
    public void ClickingOutsideDropdownDismissesWithoutClickThrough()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        var popupHost = new PopupHost();
        using var dropdown = new Dropdown<int>(
            factory,
            popupHost,
            [new("One", 1), new("Two", 2)],
            1,
            _ => { });
        var scene = new TestScene(dropdown, popupHost);
        var root = new UiRoot(scene, UiDpi.Default);
        var size = new SizeF(600.0f, 400.0f);
        root.Arrange(size);
        root.SetFocus(dropdown);
        root.HandleKey(new UiKeyEvent(UiKey.Enter), scene, wrapFocus: true, directionalNavigation: true);
        root.Arrange(size);

        Assert.IsTrue(root.HandleKey(
            new UiKeyEvent(UiKey.Escape),
            scene,
            wrapFocus: true,
            directionalNavigation: true));
        Assert.IsFalse(dropdown.IsOpen);
        root.HandleKey(new UiKeyEvent(UiKey.Enter), scene, wrapFocus: true, directionalNavigation: true);
        root.Arrange(size);

        bool pressed = root.HandlePointer(Pointer(UiPointerEventKind.Pressed, new PointF(500.0f, 350.0f)));
        bool released = root.HandlePointer(Pointer(UiPointerEventKind.Released, new PointF(500.0f, 350.0f)));

        Assert.IsTrue(pressed);
        Assert.IsTrue(released);
        Assert.IsFalse(dropdown.IsOpen);
    }

    private static UiPointerEvent Pointer(UiPointerEventKind kind, PointF position)
    {
        return new UiPointerEvent(kind, position, PointerButton.Primary);
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
}
