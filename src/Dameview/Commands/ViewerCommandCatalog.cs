using Dameview.Platform;

namespace Dameview.Commands;

internal enum ViewerCommandId
{
    NewTab,
    CloseTab,
    PreviousTab,
    NextTab,
    PreviousImage,
    NextImage,
    FitImage,
    ShowActualSize,
    SplitRight,
    SplitDown,
    EqualizePanes,
    OptimizePaneLayout,
    ShowSettings,
    ShowCommandPalette,
}

internal readonly record struct ViewerCommandShortcut(
    UiKey Key,
    bool Control = false,
    bool Shift = false)
{
    internal string DisplayText =>
        $"{(Control ? "Ctrl+" : string.Empty)}{(Shift ? "Shift+" : string.Empty)}{GetKeyLabel(Key)}";

    internal bool Matches(UiKeyEvent input) =>
        input.Key == Key
        && input.Control == Control
        && input.Shift == Shift;

    private static string GetKeyLabel(UiKey key) => key switch
    {
        UiKey.Number1 => "1",
        UiKey.Comma => ",",
        _ => key.ToString(),
    };
}

internal sealed record ViewerCommand(ViewerCommandId Id, string Label);

internal readonly record struct ViewerKeyBinding(
    ViewerCommandId Command,
    ViewerCommandShortcut Shortcut);

internal static class ViewerCommandCatalog
{
    internal static IReadOnlyList<ViewerCommand> Commands { get; } =
    [
        new(ViewerCommandId.NewTab, "New tab"),
        new(ViewerCommandId.CloseTab, "Close tab"),
        new(ViewerCommandId.PreviousTab, "Select previous tab"),
        new(ViewerCommandId.NextTab, "Select next tab"),
        new(ViewerCommandId.PreviousImage, "Previous image"),
        new(ViewerCommandId.NextImage, "Next image"),
        new(ViewerCommandId.FitImage, "Fit image"),
        new(ViewerCommandId.ShowActualSize, "Show actual size"),
        new(ViewerCommandId.SplitRight, "Split right"),
        new(ViewerCommandId.SplitDown, "Split down"),
        new(ViewerCommandId.EqualizePanes, "Equalize panes"),
        new(ViewerCommandId.OptimizePaneLayout, "Optimize pane layout"),
        new(ViewerCommandId.ShowSettings, "Open settings"),
        new(ViewerCommandId.ShowCommandPalette, "Show command palette"),
    ];
}

internal static class ViewerKeyBindings
{
    internal static IReadOnlyList<ViewerKeyBinding> Window { get; } =
    [
        new(ViewerCommandId.NewTab, new(UiKey.T, Control: true)),
        new(ViewerCommandId.CloseTab, new(UiKey.W, Control: true)),
        new(ViewerCommandId.PreviousTab, new(UiKey.Tab, Control: true, Shift: true)),
        new(ViewerCommandId.NextTab, new(UiKey.Tab, Control: true)),
        new(ViewerCommandId.SplitDown, new(UiKey.S, Control: true)),
        new(ViewerCommandId.SplitRight, new(UiKey.S, Control: true, Shift: true)),
        new(ViewerCommandId.ShowSettings, new(UiKey.Comma, Control: true)),
        new(ViewerCommandId.ShowCommandPalette, new(UiKey.P, Control: true, Shift: true)),
    ];

    internal static IReadOnlyList<ViewerKeyBinding> Viewer { get; } =
    [
        new(ViewerCommandId.PreviousImage, new(UiKey.Left)),
        new(ViewerCommandId.NextImage, new(UiKey.Right)),
        new(ViewerCommandId.FitImage, new(UiKey.F)),
        new(ViewerCommandId.ShowActualSize, new(UiKey.Number1)),
        new(ViewerCommandId.ShowActualSize, new(UiKey.Numpad1)),
    ];

    internal static bool TryGetCommand(
        IReadOnlyList<ViewerKeyBinding> bindings,
        UiKeyEvent input,
        out ViewerCommandId command)
    {
        foreach (ViewerKeyBinding binding in bindings)
        {
            if (binding.Shortcut.Matches(input))
            {
                command = binding.Command;
                return true;
            }
        }

        command = default;
        return false;
    }

    internal static ViewerCommandShortcut? GetPrimaryShortcut(ViewerCommandId command)
    {
        foreach (ViewerKeyBinding binding in Window.Concat(Viewer))
        {
            if (binding.Command == command)
            {
                return binding.Shortcut;
            }
        }

        return null;
    }
}
