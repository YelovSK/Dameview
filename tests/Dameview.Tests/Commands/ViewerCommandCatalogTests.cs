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
        // Bound here rather than relied on from the defaults, so that rebinding a shipped
        // shortcut cannot fail this test for a reason that has nothing to do with scopes.
        ViewerCommandShortcut shared = new(WindowKey.F3, Control: true);
        ViewerKeyBindings bindings = ViewerKeyBindings.Defaults
            .WithShortcut(ViewerCommandId.SplitDown, shared)
            .WithShortcut(ViewerCommandId.FitImage, shared);

        Assert.IsTrue(bindings.TryGetCommand(
            ViewerCommandScope.Window,
            new WindowKeyEvent(WindowKey.F3, Control: true),
            out ViewerCommandId window));
        Assert.AreEqual(ViewerCommandId.SplitDown, window);

        Assert.IsTrue(bindings.TryGetCommand(
            ViewerCommandScope.Viewer,
            new WindowKeyEvent(WindowKey.F3, Control: true),
            out ViewerCommandId viewer));
        Assert.AreEqual(ViewerCommandId.FitImage, viewer);

        ViewerKeyBindings unbound = bindings.WithShortcuts(ViewerCommandId.FitImage, []);
        Assert.IsFalse(unbound.TryGetCommand(
            ViewerCommandScope.Viewer,
            new WindowKeyEvent(WindowKey.F3, Control: true),
            out _));
        Assert.IsTrue(unbound.TryGetCommand(
            ViewerCommandScope.Window,
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
