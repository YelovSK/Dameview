using Dameview.Commands;
using Dameview.Platform;

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
        Assert.IsTrue(ViewerKeyBindings.TryGetCommand(
            ViewerKeyBindings.Window,
            new WindowKeyEvent(WindowKey.Tab, Shift: true, Control: true),
            out ViewerCommandId previous));
        Assert.AreEqual(ViewerCommandId.PreviousTab, previous);

        Assert.IsTrue(ViewerKeyBindings.TryGetCommand(
            ViewerKeyBindings.Window,
            new WindowKeyEvent(WindowKey.S, Control: true),
            out ViewerCommandId splitDown));
        Assert.AreEqual(ViewerCommandId.SplitDown, splitDown);

        Assert.IsTrue(ViewerKeyBindings.TryGetCommand(
            ViewerKeyBindings.Window,
            new WindowKeyEvent(WindowKey.S, Shift: true, Control: true),
            out ViewerCommandId splitRight));
        Assert.AreEqual(ViewerCommandId.SplitRight, splitRight);

        Assert.IsTrue(ViewerKeyBindings.TryGetCommand(
            ViewerKeyBindings.Viewer,
            new WindowKeyEvent(WindowKey.Left),
            out ViewerCommandId previousImage));
        Assert.AreEqual(ViewerCommandId.PreviousImage, previousImage);

        Assert.IsFalse(ViewerKeyBindings.TryGetCommand(
            ViewerKeyBindings.Window,
            new WindowKeyEvent(WindowKey.Left),
            out _));
        Assert.IsFalse(ViewerKeyBindings.TryGetCommand(
            ViewerKeyBindings.Viewer,
            new WindowKeyEvent(WindowKey.Tab, Shift: true, Control: true),
            out _));

        Assert.IsFalse(ViewerKeyBindings.TryGetCommand(
            ViewerKeyBindings.Window,
            new WindowKeyEvent(WindowKey.T, Shift: true, Control: true),
            out _));
    }

    [TestMethod]
    public void EveryBindingReferencesACatalogCommand()
    {
        ViewerCommandId[] commands = [.. ViewerCommandCatalog.Commands.Select(command => command.Id)];

        Assert.IsFalse(ViewerKeyBindings.Window.Concat(ViewerKeyBindings.Viewer)
            .Any(binding => !commands.Contains(binding.Command)));
    }

    [TestMethod]
    public void ShortcutLabelsAreDerivedFromTheirKeyChord()
    {
        Assert.AreEqual("Ctrl+,", new ViewerCommandShortcut(WindowKey.Comma, Control: true).DisplayText);
        Assert.AreEqual(
            "Ctrl+Shift+P",
            new ViewerCommandShortcut(WindowKey.P, Control: true, Shift: true).DisplayText);
        Assert.AreEqual(
            "1",
            ViewerKeyBindings.GetPrimaryShortcut(ViewerCommandId.ShowActualSize)?.DisplayText);
        Assert.AreEqual(
            "Ctrl+S",
            ViewerKeyBindings.GetPrimaryShortcut(ViewerCommandId.SplitDown)?.DisplayText);
        Assert.AreEqual(
            "Ctrl+Shift+S",
            ViewerKeyBindings.GetPrimaryShortcut(ViewerCommandId.SplitRight)?.DisplayText);
    }
}
