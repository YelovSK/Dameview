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
        var panel = new CommandPalettePanel(commands, ViewerKeyBindings.Defaults, command => executed = command, _ => { });
        var root = new UiRoot(panel, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(540.0f, 580.0f));
        root.SetFocus(panel.InitialFocus);

        root.HandleKey(new WindowKeyEvent(WindowKey.Down), panel, wrapFocus: true, directionalNavigation: true);
        Assert.AreSame(panel.InitialFocus, root.FocusedElement);
        root.HandleKey(new WindowKeyEvent(WindowKey.Enter), panel, wrapFocus: true, directionalNavigation: true);

        Assert.AreEqual(ViewerCommandId.CloseTab, executed);
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
        var panel = new CommandPalettePanel(commands, ViewerKeyBindings.Defaults, _ => { }, _ => { });
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
        var panel = new CommandPalettePanel(commands, ViewerKeyBindings.Defaults, command => executed = command, _ => { });
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
        Assert.IsTrue(panel.IsCapturing);
        Assert.IsTrue(panel.HandleCaptureKey(new WindowKeyEvent(WindowKey.G, Control: true)));

        Assert.IsFalse(panel.IsCapturing);
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
        Assert.IsTrue(panel.HandleCaptureKey(new WindowKeyEvent(WindowKey.Escape)));

        Assert.IsFalse(panel.IsCapturing);
        Assert.IsNull(applied, "Cancelling records nothing.");
    }

    [TestMethod]
    public void DeleteUnbindsTheSlot()
    {
        ViewerKeyBindings? applied = null;
        CommandPalettePanel panel = CreatePanel(bindings => applied = bindings, out UiRoot root);

        BeginCapture(root, panel);
        Assert.IsTrue(panel.HandleCaptureKey(new WindowKeyEvent(WindowKey.Delete)));

        Assert.IsFalse(panel.IsCapturing);
        Assert.IsNotNull(applied);
        Assert.IsEmpty(applied.GetShortcuts(ViewerCommandId.NewTab));
    }

    [TestMethod]
    public void AKeyWithNoTextFormIsSwallowedRatherThanBound()
    {
        ViewerKeyBindings? applied = null;
        CommandPalettePanel panel = CreatePanel(bindings => applied = bindings, out UiRoot root);

        BeginCapture(root, panel);

        // A key the enum does not name could not be written to settings, so it is ignored
        // without ending the capture.
        Assert.IsTrue(panel.HandleCaptureKey(new WindowKeyEvent((WindowKey)9999)));

        Assert.IsTrue(panel.IsCapturing, "The capture waits for a key it can store.");
        Assert.IsNull(applied);
    }

    [TestMethod]
    public void CaptureKeysAreIgnoredWhileNothingIsRecording()
    {
        CommandPalettePanel panel = CreatePanel(_ => { }, out _);

        Assert.IsFalse(panel.HandleCaptureKey(new WindowKeyEvent(WindowKey.G, Control: true)));
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
