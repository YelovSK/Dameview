using System.Drawing;
using Dameview.Commands;
using Dameview.Platform;
using Dameview.UI;
using Dameview.UI.Panels;
using Vortice.DirectWrite;
using static Vortice.DirectWrite.DWrite;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class CommandPalettePanelTests
{
    [TestMethod]
    public void ArrowKeysSelectACommandAndEnterExecutesIt()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        ViewerCommandId? executed = null;
        ViewerCommand[] commands =
        [
            new(ViewerCommandId.NewTab, "New tab"),
            new(ViewerCommandId.CloseTab, "Close tab"),
        ];
        using var panel = new CommandPalettePanel(factory, commands, command => executed = command);
        var root = new UiRoot(panel, UiDpi.Default);
        root.Arrange(new SizeF(540.0f, 580.0f));
        root.SetFocus(panel.InitialFocus);

        root.HandleKey(new UiKeyEvent(UiKey.Down), panel, wrapFocus: true, directionalNavigation: true);
        Assert.AreSame(panel.InitialFocus, root.FocusedElement);
        root.HandleKey(new UiKeyEvent(UiKey.Enter), panel, wrapFocus: true, directionalNavigation: true);

        Assert.AreEqual(ViewerCommandId.CloseTab, executed);
    }

    [TestMethod]
    public void TextInputFiltersImmediatelyAndKeepsTheFirstResultSelected()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        ViewerCommand[] commands =
        [
            new(ViewerCommandId.NewTab, "Alpha"),
            new(ViewerCommandId.CloseTab, "Alpine"),
            new(ViewerCommandId.ShowSettings, "Beta"),
        ];
        using var panel = new CommandPalettePanel(factory, commands, _ => { });
        var root = new UiRoot(panel, UiDpi.Default);
        root.Arrange(new SizeF(540.0f, 580.0f));
        root.SetFocus(panel.InitialFocus);

        root.HandleTextInput("a");
        Assert.AreEqual(3, panel.VisibleCommandCount);
        root.HandleTextInput("l");

        Assert.AreEqual("al", panel.Query);
        Assert.AreEqual(2, panel.VisibleCommandCount);
        Assert.AreEqual(ViewerCommandId.NewTab, panel.SelectedCommand);
    }

    [TestMethod]
    public void EmptyResultsCannotExecuteAndResetRestoresTheCatalog()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        ViewerCommandId? executed = null;
        ViewerCommand[] commands =
        [
            new(ViewerCommandId.NewTab, "New tab"),
            new(ViewerCommandId.CloseTab, "Close tab"),
        ];
        using var panel = new CommandPalettePanel(factory, commands, command => executed = command);
        var root = new UiRoot(panel, UiDpi.Default);
        root.Arrange(new SizeF(540.0f, 580.0f));
        root.SetFocus(panel.InitialFocus);

        root.HandleTextInput("z");
        root.HandleKey(new UiKeyEvent(UiKey.Enter), panel, wrapFocus: true, directionalNavigation: true);

        Assert.AreEqual(0, panel.VisibleCommandCount);
        Assert.IsNull(panel.SelectedCommand);
        Assert.IsNull(executed);

        panel.Reset();

        Assert.AreEqual(string.Empty, panel.Query);
        Assert.AreEqual(2, panel.VisibleCommandCount);
        Assert.AreEqual(ViewerCommandId.NewTab, panel.SelectedCommand);
    }
}
