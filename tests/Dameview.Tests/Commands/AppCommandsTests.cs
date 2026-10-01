using System.Reflection;
using Dameview.Commands;
using Dameview.Win32.Input;

namespace Dameview.Tests.Commands;

[TestClass]
public sealed class AppCommandsTests
{
    [TestMethod]
    public void EveryCommandIsListedOnceWithALabel()
    {
        Command[] declared =
        [
            .. typeof(AppCommands)
                .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(field => field.FieldType == typeof(Command))
                .Select(field => (Command)field.GetValue(null)!),
        ];

        CollectionAssert.AreEquivalent(declared, AppCommands.All.ToArray());
        Assert.HasCount(AppCommands.All.Count, AppCommands.All.Select(command => command.Id).Distinct());
        Assert.IsFalse(AppCommands.All.Any(command => string.IsNullOrWhiteSpace(command.Label)));
    }

    // The ids name key bindings in the settings file, so renaming one loses a user's shortcuts.
    [TestMethod]
    public void CommandIdsStayAsTheSettingsFileKnowsThem()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "openFile", "newTab", "closeTab", "reopenClosedTab", "previousTab", "nextTab",
                "previousImage", "nextImage", "fitImage", "showActualSize", "toggleFitActualSize",
                "rotateLeft", "rotateRight", "flipHorizontal", "flipVertical", "copyImage", "copyFilePath", "copyFile", "showInFolder", "openWith", "showProperties",
                "deleteFile", "toggleFullscreen", "toggleGallery", "toggleFlattenFolder", "splitRight",
                "splitDown", "toggleViewportSync", "balancePanes", "optimizePaneLayout", "togglePerformanceOverlay",
                "showSettings", "openDataFolder", "showCommandPalette",
            },
            AppCommands.All.Select(command => command.Id).ToArray());
    }

    [TestMethod]
    public void BindingsResolveOnlyWithinTheirOwnScope()
    {
        // Bound here rather than relied on from the defaults, so that rebinding a shipped
        // shortcut cannot fail this test for a reason that has nothing to do with scopes.
        ViewerCommandShortcut shared = new(WindowKey.F3, Control: true);
        ViewerKeyBindings bindings = ViewerKeyBindings.Defaults
            .WithShortcut(AppCommands.SplitDown, shared)
            .WithShortcut(AppCommands.FitImage, shared);

        Assert.IsTrue(bindings.TryGetCommand(
            CommandScope.Window,
            new WindowKeyEvent(WindowKey.F3, Control: true),
            out Command? window));
        Assert.AreEqual(AppCommands.SplitDown, window);

        Assert.IsTrue(bindings.TryGetCommand(
            CommandScope.Viewer,
            new WindowKeyEvent(WindowKey.F3, Control: true),
            out Command? viewer));
        Assert.AreEqual(AppCommands.FitImage, viewer);

        ViewerKeyBindings unbound = bindings.WithShortcuts(AppCommands.FitImage, []);
        Assert.IsFalse(unbound.TryGetCommand(
            CommandScope.Viewer,
            new WindowKeyEvent(WindowKey.F3, Control: true),
            out _));
        Assert.IsTrue(unbound.TryGetCommand(
            CommandScope.Window,
            new WindowKeyEvent(WindowKey.F3, Control: true),
            out _),
            "Unbinding in one scope leaves the other scope's command alone.");
    }

    [TestMethod]
    public void TextThatNamesNoKeyOrModifierIsRejected()
    {
        Assert.IsTrue(ViewerCommandShortcut.TryParse("Ctrl+,", out ViewerCommandShortcut settings));
        Assert.AreEqual(new ViewerCommandShortcut(WindowKey.Comma, Control: true), settings);

        Assert.IsFalse(ViewerCommandShortcut.TryParse("Ctrl+NotAKey", out _));
        Assert.IsFalse(ViewerCommandShortcut.TryParse("Hyper+A", out _));
    }

    [TestMethod]
    public void AssigningAShortcutTakesItFromTheCommandThatHeldIt()
    {
        ViewerCommandShortcut newTab = new(WindowKey.T, Control: true);
        ViewerKeyBindings bindings = ViewerKeyBindings.Defaults.WithShortcut(AppCommands.CloseTab, newTab);

        Assert.IsFalse(ViewerKeyBindings.Defaults.GetShortcuts(AppCommands.NewTab).Count == 0);
        Assert.IsEmpty(bindings.GetShortcuts(AppCommands.NewTab));
        Assert.Contains(newTab, bindings.GetShortcuts(AppCommands.CloseTab));
        Assert.IsTrue(bindings.TryGetCommand(CommandScope.Window, new WindowKeyEvent(WindowKey.T, Control: true), out Command? command));
        Assert.AreEqual(AppCommands.CloseTab, command);
    }

    [TestMethod]
    public void AssigningAShortcutACommandAlreadyHasDoesNotRepeatIt()
    {
        ViewerCommandShortcut newTab = ViewerKeyBindings.Defaults.GetShortcuts(AppCommands.NewTab)[0];
        ViewerKeyBindings bindings = ViewerKeyBindings.Defaults
            .WithShortcut(AppCommands.NewTab, newTab);

        Assert.HasCount(1, bindings.GetShortcuts(AppCommands.NewTab).Where(s => s == newTab));
    }

    [TestMethod]
    public void TheSameShortcutInADifferentScopeIsNotAConflict()
    {
        // FitImage is a Viewer command and NewTab a Window one, so Ctrl+T can serve both.
        ViewerCommandShortcut shortcut = new(WindowKey.T, Control: true);
        ViewerKeyBindings bindings = ViewerKeyBindings.Defaults.WithShortcut(AppCommands.FitImage, shortcut);

        Assert.Contains(shortcut, bindings.GetShortcuts(AppCommands.NewTab));
        Assert.Contains(shortcut, bindings.GetShortcuts(AppCommands.FitImage));
    }

    [TestMethod]
    public void ShortcutLabelsAreDerivedFromTheirKeyChord()
    {
        // Modifier order, the named-key table, and a key that falls through to its enum name.
        Assert.AreEqual("Ctrl+,", new ViewerCommandShortcut(WindowKey.Comma, Control: true).Text);
        Assert.AreEqual(
            "Ctrl+Shift+P",
            new ViewerCommandShortcut(WindowKey.P, Control: true, Shift: true).Text);
        Assert.AreEqual("1", new ViewerCommandShortcut(WindowKey.Number1).Text);
        Assert.AreEqual("Numpad1", new ViewerCommandShortcut(WindowKey.Numpad1).Text);
        Assert.AreEqual("F11", new ViewerCommandShortcut(WindowKey.F11).Text);
    }

    // 89 keys times four modifier combinations is small enough to check outright, so no
    // shortcut can reach settings in a form that cannot be read back.
    [TestMethod]
    public void EveryKeyAndModifierCombinationRoundTripsThroughItsText()
    {
        foreach (WindowKey key in Enum.GetValues<WindowKey>())
        {
            foreach (bool control in new[] { false, true })
            {
                foreach (bool shift in new[] { false, true })
                {
                    var shortcut = new ViewerCommandShortcut(key, control, shift);
                    string text = shortcut.Text;

                    Assert.IsTrue(
                        ViewerCommandShortcut.TryParse(text, out ViewerCommandShortcut parsed),
                        $"{key} control={control} shift={shift} produced unparsable '{text}'.");
                    Assert.AreEqual(shortcut, parsed, text);
                }
            }
        }
    }
}
