using Dameview.Commands;
using Dameview.Win32.Input;

namespace Dameview.Tests.Commands;

[TestClass]
public sealed class ViewerCommandCatalogTests
{
    [TestMethod]
    public void CatalogDefinesEveryCommandExactlyOnce()
    {
        ViewerCommandId[] expected = Enum.GetValues<ViewerCommandId>();
        ViewerCommandId[] actual = [.. ViewerCommandCatalog.Commands.Select(command => command.Id)];

        CollectionAssert.AreEquivalent(expected, actual);
        Assert.HasCount(expected.Length, actual.Distinct());
        Assert.IsFalse(ViewerCommandCatalog.Commands.Any(command => string.IsNullOrWhiteSpace(command.Label)));
    }

    [TestMethod]
    public void BindingsResolveOnlyWithinTheirOwnScope()
    {
        Assert.IsTrue(ViewerKeyBindings.Defaults.TryGetCommand(
            ViewerCommandScope.Window,
            new WindowKeyEvent(WindowKey.Tab, Shift: true, Control: true),
            out ViewerCommandId previous));
        Assert.AreEqual(ViewerCommandId.PreviousTab, previous);

        Assert.IsTrue(ViewerKeyBindings.Defaults.TryGetCommand(
            ViewerCommandScope.Window,
            new WindowKeyEvent(WindowKey.S, Control: true),
            out ViewerCommandId splitDown));
        Assert.AreEqual(ViewerCommandId.SplitDown, splitDown);

        Assert.IsTrue(ViewerKeyBindings.Defaults.TryGetCommand(
            ViewerCommandScope.Window,
            new WindowKeyEvent(WindowKey.S, Shift: true, Control: true),
            out ViewerCommandId splitRight));
        Assert.AreEqual(ViewerCommandId.SplitRight, splitRight);

        Assert.IsTrue(ViewerKeyBindings.Defaults.TryGetCommand(
            ViewerCommandScope.Window,
            new WindowKeyEvent(WindowKey.F11),
            out ViewerCommandId fullscreen));
        Assert.AreEqual(ViewerCommandId.ToggleFullscreen, fullscreen);

        Assert.IsTrue(ViewerKeyBindings.Defaults.TryGetCommand(
            ViewerCommandScope.Window,
            new WindowKeyEvent(WindowKey.F, Control: true),
            out fullscreen));
        Assert.AreEqual(ViewerCommandId.ToggleFullscreen, fullscreen);

        Assert.IsTrue(ViewerKeyBindings.Defaults.TryGetCommand(
            ViewerCommandScope.Viewer,
            new WindowKeyEvent(WindowKey.Left),
            out ViewerCommandId previousImage));
        Assert.AreEqual(ViewerCommandId.PreviousImage, previousImage);

        Assert.IsTrue(ViewerKeyBindings.Defaults.TryGetCommand(
            ViewerCommandScope.Viewer,
            new WindowKeyEvent(WindowKey.C, Control: true),
            out ViewerCommandId copyImage));
        Assert.AreEqual(ViewerCommandId.CopyImage, copyImage);

        Assert.IsFalse(ViewerKeyBindings.Defaults.TryGetCommand(
            ViewerCommandScope.Window,
            new WindowKeyEvent(WindowKey.Left),
            out _));
        Assert.IsFalse(ViewerKeyBindings.Defaults.TryGetCommand(
            ViewerCommandScope.Viewer,
            new WindowKeyEvent(WindowKey.Tab, Shift: true, Control: true),
            out _));

        Assert.IsFalse(ViewerKeyBindings.Defaults.TryGetCommand(
            ViewerCommandScope.Window,
            new WindowKeyEvent(WindowKey.W, Shift: true, Control: true),
            out _));
    }

    [TestMethod]
    public void ShortcutsRoundTripThroughTheirText()
    {
        foreach (ViewerCommand command in ViewerCommandCatalog.Commands)
        {
            foreach (ViewerCommandShortcut shortcut in ViewerKeyBindings.Defaults.GetShortcuts(command.Id))
            {
                Assert.IsTrue(
                    ViewerCommandShortcut.TryParse(shortcut.Text, out ViewerCommandShortcut parsed),
                    shortcut.Text);
                Assert.AreEqual(shortcut, parsed);
            }
        }

        Assert.IsTrue(ViewerCommandShortcut.TryParse("Ctrl+,", out ViewerCommandShortcut settings));
        Assert.AreEqual(new ViewerCommandShortcut(WindowKey.Comma, Control: true), settings);

        Assert.IsFalse(ViewerCommandShortcut.TryParse("Ctrl+NotAKey", out _));
        Assert.IsFalse(ViewerCommandShortcut.TryParse("Hyper+A", out _));
    }

    [TestMethod]
    public void AssigningAShortcutTakesItFromTheCommandThatHeldIt()
    {
        ViewerCommandShortcut newTab = new(WindowKey.T, Control: true);
        ViewerKeyBindings bindings = ViewerKeyBindings.Defaults.WithShortcut(ViewerCommandId.CloseTab, newTab);

        Assert.IsFalse(ViewerKeyBindings.Defaults.GetShortcuts(ViewerCommandId.NewTab).Count == 0);
        Assert.IsEmpty(bindings.GetShortcuts(ViewerCommandId.NewTab));
        Assert.Contains(newTab, bindings.GetShortcuts(ViewerCommandId.CloseTab));
        Assert.IsTrue(bindings.TryGetCommand(ViewerCommandScope.Window, new WindowKeyEvent(WindowKey.T, Control: true), out ViewerCommandId command));
        Assert.AreEqual(ViewerCommandId.CloseTab, command);
    }

    [TestMethod]
    public void TheSameShortcutInADifferentScopeIsNotAConflict()
    {
        // FitImage is a Viewer command and NewTab a Window one, so Ctrl+T can serve both.
        ViewerCommandShortcut shortcut = new(WindowKey.T, Control: true);
        ViewerKeyBindings bindings = ViewerKeyBindings.Defaults.WithShortcut(ViewerCommandId.FitImage, shortcut);

        Assert.Contains(shortcut, bindings.GetShortcuts(ViewerCommandId.NewTab));
        Assert.Contains(shortcut, bindings.GetShortcuts(ViewerCommandId.FitImage));
    }

    [TestMethod]
    public void ShortcutLabelsAreDerivedFromTheirKeyChord()
    {
        Assert.AreEqual("Ctrl+,", new ViewerCommandShortcut(WindowKey.Comma, Control: true).Text);
        Assert.AreEqual(
            "Ctrl+Shift+P",
            new ViewerCommandShortcut(WindowKey.P, Control: true, Shift: true).Text);
        string[] expectedActualSize = ["1", "Numpad1"];
        CollectionAssert.AreEqual(
            expectedActualSize,
            ViewerKeyBindings.Defaults.GetShortcuts(ViewerCommandId.ShowActualSize)
                .Select(shortcut => shortcut.Text)
                .ToArray());
        Assert.AreEqual(
            "Ctrl+S",
            ViewerKeyBindings.Defaults.GetShortcuts(ViewerCommandId.SplitDown).Single().Text);
        Assert.AreEqual(
            "Ctrl+Shift+S",
            ViewerKeyBindings.Defaults.GetShortcuts(ViewerCommandId.SplitRight).Single().Text);
        string[] expectedFullscreen = ["F11", "Ctrl+F"];
        CollectionAssert.AreEqual(
            expectedFullscreen,
            ViewerKeyBindings.Defaults.GetShortcuts(ViewerCommandId.ToggleFullscreen)
                .Select(shortcut => shortcut.Text)
                .ToArray());
    }
}
