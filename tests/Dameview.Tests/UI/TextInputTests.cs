using System.Drawing;
using Dameview.Platform;
using Dameview.UI;
using Dameview.UI.Components;
using Vortice.DirectWrite;
using static Vortice.DirectWrite.DWrite;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class TextInputTests
{
    [TestMethod]
    public void FocusedInputReceivesTextAndRaisesEachChangeImmediately()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        using var input = new TextInput(factory, "Filter commands");
        var root = new UiRoot(input, UiDpi.Default);
        var changes = new List<string>();
        input.TextChanged += changes.Add;
        root.Arrange(new SizeF(300.0f, 38.0f));
        root.SetFocus(input);

        Assert.IsTrue(root.HandleTextInput("a"));
        Assert.IsTrue(root.HandleTextInput("ž"));

        Assert.AreEqual("až", input.Text);
        Assert.HasCount(2, changes);
        Assert.AreEqual("a", changes[0]);
        Assert.AreEqual("až", changes[1]);
    }

    [TestMethod]
    public void BackspaceAndDeleteOperateOnWholeTextElements()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        using var input = new TextInput(factory) { Text = "A😀e\u0301" };

        Assert.IsTrue(input.OnKeyEvent(new UiKeyEvent(UiKey.Backspace)));
        Assert.AreEqual("A😀", input.Text);
        Assert.IsTrue(input.OnKeyEvent(new UiKeyEvent(UiKey.Backspace)));
        Assert.AreEqual("A", input.Text);

        input.OnKeyEvent(new UiKeyEvent(UiKey.Home));
        input.OnKeyEvent(new UiKeyEvent(UiKey.Delete));

        Assert.AreEqual(string.Empty, input.Text);
        Assert.AreEqual(0, input.CaretIndex);
    }

    [TestMethod]
    public void UnhandledKeysBubbleFromTheInputToItsParent()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        using var input = new TextInput(factory);
        var parent = new KeyContainer(input);
        var root = new UiRoot(parent, UiDpi.Default);
        root.Arrange(new SizeF(300.0f, 100.0f));
        root.SetFocus(input);

        Assert.IsTrue(root.HandleKey(
            new UiKeyEvent(UiKey.Down),
            parent,
            wrapFocus: false,
            directionalNavigation: false));
        Assert.AreEqual(UiKey.Down, parent.LastKey);
    }

    private sealed class KeyContainer : UiElement
    {
        private readonly TextInput _input;

        internal KeyContainer(TextInput input)
        {
            _input = input;
            AddChild(input);
        }

        internal UiKey? LastKey { get; private set; }

        internal override bool OnKeyEvent(UiKeyEvent input)
        {
            LastKey = input.Key;
            return true;
        }

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            _input.Measure(new SizeF(availableSize.Width, 38.0f));
            return availableSize;
        }

        protected override void ArrangeCore(SizeF finalSize)
        {
            _input.Arrange(new RectangleF(0.0f, 0.0f, finalSize.Width, 38.0f));
        }
    }
}
