using System.Drawing;
using Dameview.Commands;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Panels;
using Dameview.Win32.Input;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class CommandPalettePanelTests
{
    [TestMethod]
    public void ArrowKeysSelectACommandAndEnterExecutesIt()
    {
        ViewerCommandId? executed = null;
        ViewerCommand[] commands =
        [
            new(ViewerCommandId.NewTab, "New tab", ViewerCommandScope.Window),
            new(ViewerCommandId.CloseTab, "Close tab", ViewerCommandScope.Window),
        ];
        var panel = new CommandPalettePanel(commands, ViewerKeyBindings.Defaults, command => executed = command, _ => true, _ => { });
        var root = new UiRoot(panel, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(540.0f, 580.0f));
        root.SetFocus(panel.InitialFocus);

        root.HandleKey(new WindowKeyEvent(WindowKey.Down), panel, wrapFocus: true, directionalNavigation: true);
        Assert.AreSame(panel.InitialFocus, root.FocusedElement);
        root.HandleKey(new WindowKeyEvent(WindowKey.Enter), panel, wrapFocus: true, directionalNavigation: true);

        Assert.AreEqual(ViewerCommandId.CloseTab, executed);
    }

    [TestMethod]
    public void CommandsThatCannotRunAreListedLastAndCannotBeSelected()
    {
        ViewerCommandId? executed = null;
        ViewerCommand[] commands =
        [
            new(ViewerCommandId.CopyImage, "Copy image", ViewerCommandScope.Viewer),
            new(ViewerCommandId.NewTab, "New tab", ViewerCommandScope.Window),
            new(ViewerCommandId.CloseTab, "Close tab", ViewerCommandScope.Window),
        ];
        var panel = new CommandPalettePanel(
            commands,
            ViewerKeyBindings.Defaults,
            command => executed = command,
            command => command != ViewerCommandId.CopyImage,
            _ => { });
        var root = new UiRoot(panel, UiDpi.Default, TestTextLayouts.Shared);
        panel.Reset();
        root.Arrange(new SizeF(540.0f, 580.0f));
        root.SetFocus(panel.InitialFocus);

        Assert.AreEqual(3, panel.MatchingCommandCount, "Commands that cannot run stay listed.");
        Assert.AreEqual(ViewerCommandId.NewTab, panel.SelectedCommand);
        root.HandleKey(new WindowKeyEvent(WindowKey.Down), panel, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Down), panel, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Enter), panel, wrapFocus: true, directionalNavigation: true);

        Assert.AreEqual(ViewerCommandId.NewTab, executed, "Selection wraps past the command that cannot run.");
    }

    [TestMethod]
    public void TextInputFiltersImmediatelyAndKeepsTheFirstResultSelected()
    {
        ViewerCommand[] commands =
        [
            new(ViewerCommandId.NewTab, "Alpha", ViewerCommandScope.Window),
            new(ViewerCommandId.CloseTab, "Alpine", ViewerCommandScope.Window),
            new(ViewerCommandId.ShowSettings, "Beta", ViewerCommandScope.Window),
        ];
        var panel = new CommandPalettePanel(commands, ViewerKeyBindings.Defaults, _ => { }, _ => true, _ => { });
        var root = new UiRoot(panel, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(540.0f, 580.0f));
        root.SetFocus(panel.InitialFocus);

        root.HandleTextInput("a");
        Assert.AreEqual(3, panel.MatchingCommandCount);
        root.HandleTextInput("l");

        Assert.AreEqual("al", panel.Query);
        Assert.AreEqual(2, panel.MatchingCommandCount);
        Assert.AreEqual(ViewerCommandId.NewTab, panel.SelectedCommand);
    }

    [TestMethod]
    public void EmptyResultsCannotExecuteAndResetRestoresTheCatalog()
    {
        ViewerCommandId? executed = null;
        ViewerCommand[] commands =
        [
            new(ViewerCommandId.NewTab, "New tab", ViewerCommandScope.Window),
            new(ViewerCommandId.CloseTab, "Close tab", ViewerCommandScope.Window),
        ];
        var panel = new CommandPalettePanel(commands, ViewerKeyBindings.Defaults, command => executed = command, _ => true, _ => { });
        var root = new UiRoot(panel, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(540.0f, 580.0f));
        root.SetFocus(panel.InitialFocus);

        root.HandleTextInput("z");
        root.HandleKey(new WindowKeyEvent(WindowKey.Enter), panel, wrapFocus: true, directionalNavigation: true);

        Assert.AreEqual(0, panel.MatchingCommandCount);
        Assert.IsNull(panel.SelectedCommand);
        Assert.IsNull(executed);

        panel.Reset();

        Assert.AreEqual(string.Empty, panel.Query);
        Assert.AreEqual(2, panel.MatchingCommandCount);
        Assert.AreEqual(ViewerCommandId.NewTab, panel.SelectedCommand);
    }
    [TestMethod]
    public void RecordingReplacesTheSlotTheCaptureStartedFrom()
    {
        ViewerKeyBindings? applied = null;
        CommandPalettePanel panel = CreatePanel(bindings => applied = bindings, out UiRoot root);

        BeginCapture(root, panel);
        Assert.IsTrue(panel.IsRecording);
        Assert.IsTrue(root.HandleCapturedKey(new WindowKeyEvent(WindowKey.G, Control: true)));

        Assert.IsFalse(panel.IsRecording);
        Assert.IsNotNull(applied);
        CollectionAssert.AreEqual(
            new[] { new ViewerCommandShortcut(WindowKey.G, Control: true) },
            applied.GetShortcuts(ViewerCommandId.NewTab).ToArray());
    }

    [TestMethod]
    public void EscapeLeavesTheShortcutAsItWas()
    {
        ViewerKeyBindings? applied = null;
        CommandPalettePanel panel = CreatePanel(bindings => applied = bindings, out UiRoot root);

        BeginCapture(root, panel);
        Assert.IsTrue(root.HandleCapturedKey(new WindowKeyEvent(WindowKey.Escape)));

        Assert.IsFalse(panel.IsRecording);
        Assert.IsNull(applied, "Cancelling records nothing.");
    }

    [TestMethod]
    public void PressingOutsideTheRecordingChipCancelsTheCapture()
    {
        ViewerKeyBindings? applied = null;
        CommandPalettePanel panel = CreatePanel(bindings => applied = bindings, out UiRoot root);
        BeginCapture(root, panel);
        root.Arrange(new SizeF(540.0f, 580.0f));
        ShortcutChip chip = Descendants(panel).OfType<ShortcutChip>().First();
        RectangleF chipBounds = chip.GetBoundsRelativeTo(panel);

        var onChip = new PointF(chipBounds.Left + 4.0f, chipBounds.Top + (chipBounds.Height / 2.0f));
        root.HandlePointer(new WindowPointerEvent(WindowPointerEventKind.Pressed, onChip, PointerButton.Primary));
        root.HandlePointer(new WindowPointerEvent(WindowPointerEventKind.Released, onChip, PointerButton.Primary));
        Assert.IsTrue(panel.IsRecording, "Clicking the chip being recorded keeps recording.");

        root.HandlePointer(new WindowPointerEvent(
            WindowPointerEventKind.Pressed,
            new PointF(10.0f, 10.0f),
            PointerButton.Primary));
        Assert.IsFalse(panel.IsRecording);
        Assert.IsNull(applied, "Cancelling records nothing.");
    }

    [TestMethod]
    public void AddingAShortcutKeepsItsChipOnlyWhenAKeyIsRecorded()
    {
        ViewerKeyBindings? applied = null;
        CommandPalettePanel panel = CreatePanel(bindings => applied = bindings, out UiRoot root);
        Button add = Descendants(panel).OfType<Button>().Single();
        int chips = Descendants(panel).OfType<ShortcutChip>().Count();

        root.SetFocus(add);
        root.HandleKey(new WindowKeyEvent(WindowKey.Enter), panel, wrapFocus: true, directionalNavigation: true);
        Assert.IsTrue(panel.IsRecording);
        Assert.AreEqual(chips + 1, Descendants(panel).OfType<ShortcutChip>().Count());
        root.HandleCapturedKey(new WindowKeyEvent(WindowKey.Escape));
        Assert.AreEqual(chips, Descendants(panel).OfType<ShortcutChip>().Count(), "An abandoned chip goes away.");
        Assert.IsNull(applied);

        root.SetFocus(add);
        root.HandleKey(new WindowKeyEvent(WindowKey.Enter), panel, wrapFocus: true, directionalNavigation: true);
        root.HandleCapturedKey(new WindowKeyEvent(WindowKey.G, Control: true));
        Assert.IsFalse(panel.IsRecording);
        Assert.AreEqual(chips + 1, Descendants(panel).OfType<ShortcutChip>().Count());
        Assert.IsNotNull(applied);
        Assert.AreEqual(
            new ViewerCommandShortcut(WindowKey.G, Control: true),
            applied.GetShortcuts(ViewerCommandId.NewTab)[^1]);
    }

    [TestMethod]
    public void DeleteIsRecordedLikeAnyOtherKey()
    {
        ViewerKeyBindings? applied = null;
        CommandPalettePanel panel = CreatePanel(bindings => applied = bindings, out UiRoot root);

        BeginCapture(root, panel);
        Assert.IsTrue(root.HandleCapturedKey(new WindowKeyEvent(WindowKey.Delete)));

        Assert.IsNotNull(applied);
        CollectionAssert.AreEqual(
            new[] { new ViewerCommandShortcut(WindowKey.Delete) },
            applied.GetShortcuts(ViewerCommandId.NewTab).ToArray());
    }

    [TestMethod]
    public void AKeyWithNoTextFormIsSwallowedRatherThanBound()
    {
        ViewerKeyBindings? applied = null;
        CommandPalettePanel panel = CreatePanel(bindings => applied = bindings, out UiRoot root);

        BeginCapture(root, panel);

        // A key the enum does not name could not be written to settings, so it is ignored
        // without ending the capture.
        Assert.IsTrue(root.HandleCapturedKey(new WindowKeyEvent((WindowKey)9999)));

        Assert.IsTrue(panel.IsRecording, "The capture waits for a key it can store.");
        Assert.IsNull(applied);
    }

    [TestMethod]
    public void CaptureKeysAreIgnoredWhileNothingIsRecording()
    {
        CreatePanel(_ => { }, out UiRoot root);

        Assert.IsFalse(root.HandleCapturedKey(new WindowKeyEvent(WindowKey.G, Control: true)));
    }

    private static CommandPalettePanel CreatePanel(
        Action<ViewerKeyBindings> applyKeyBindings,
        out UiRoot root)
    {
        ViewerCommand[] commands = [new(ViewerCommandId.NewTab, "New tab", ViewerCommandScope.Window)];
        var panel = new CommandPalettePanel(
            commands,
            ViewerKeyBindings.Defaults,
            _ => { },
            _ => true,
            applyKeyBindings);
        root = new UiRoot(panel, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(540.0f, 580.0f));
        return panel;
    }

    private static void BeginCapture(UiRoot root, CommandPalettePanel panel)
    {
        ShortcutChip chip = Descendants(panel).OfType<ShortcutChip>().First();
        root.SetFocus(chip);
        root.HandleKey(new WindowKeyEvent(WindowKey.Enter), panel, wrapFocus: true, directionalNavigation: true);
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
