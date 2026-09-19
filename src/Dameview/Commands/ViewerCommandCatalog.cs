using Dameview.Win32.Input;

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
    CopyImage,
    ToggleFullscreen,
    SplitRight,
    SplitDown,
    BalancePanes,
    OptimizePaneLayout,
    TogglePerformanceOverlay,
    ShowSettings,
    ShowCommandPalette,
}

internal readonly record struct ViewerCommandShortcut(
    WindowKey Key,
    bool Control = false,
    bool Shift = false)
{
    internal string DisplayText =>
        $"{(Control ? "Ctrl+" : string.Empty)}{(Shift ? "Shift+" : string.Empty)}{GetKeyLabel(Key)}";

    internal bool Matches(WindowKeyEvent input) =>
        input.Key == Key
        && input.Control == Control
        && input.Shift == Shift;

    private static string GetKeyLabel(WindowKey key) => key switch
    {
        WindowKey.Number1 => "1",
        WindowKey.Comma => ",",
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
        new(ViewerCommandId.CopyImage, "Copy image"),
        new(ViewerCommandId.ToggleFullscreen, "Toggle fullscreen"),
        new(ViewerCommandId.SplitRight, "Split right"),
        new(ViewerCommandId.SplitDown, "Split down"),
        new(ViewerCommandId.BalancePanes, "Balance pane layout"),
        new(ViewerCommandId.OptimizePaneLayout, "Optimize pane layout"),
        new(ViewerCommandId.TogglePerformanceOverlay, "Toggle performance overlay"),
        new(ViewerCommandId.ShowSettings, "Open settings"),
        new(ViewerCommandId.ShowCommandPalette, "Show command palette"),
    ];
}

internal static class ViewerKeyBindings
{
    internal static IReadOnlyList<ViewerKeyBinding> Window { get; } =
    [
        new(ViewerCommandId.NewTab, new(WindowKey.T, Control: true)),
        new(ViewerCommandId.CloseTab, new(WindowKey.W, Control: true)),
        new(ViewerCommandId.PreviousTab, new(WindowKey.Tab, Control: true, Shift: true)),
        new(ViewerCommandId.NextTab, new(WindowKey.Tab, Control: true)),
        new(ViewerCommandId.SplitDown, new(WindowKey.S, Control: true)),
        new(ViewerCommandId.SplitRight, new(WindowKey.S, Control: true, Shift: true)),
        new(ViewerCommandId.ShowSettings, new(WindowKey.Comma, Control: true)),
        new(ViewerCommandId.ShowCommandPalette, new(WindowKey.P, Control: true, Shift: true)),
        new(ViewerCommandId.TogglePerformanceOverlay, new(WindowKey.F3)),
        new(ViewerCommandId.ToggleFullscreen, new(WindowKey.F11)),
        new(ViewerCommandId.ToggleFullscreen, new(WindowKey.F, Control: true)),
    ];

    internal static IReadOnlyList<ViewerKeyBinding> Viewer { get; } =
    [
        new(ViewerCommandId.PreviousImage, new(WindowKey.Left)),
        new(ViewerCommandId.NextImage, new(WindowKey.Right)),
        new(ViewerCommandId.FitImage, new(WindowKey.F)),
        new(ViewerCommandId.ShowActualSize, new(WindowKey.Number1)),
        new(ViewerCommandId.ShowActualSize, new(WindowKey.Numpad1)),
        new(ViewerCommandId.CopyImage, new(WindowKey.C, Control: true)),
    ];

    internal static bool TryGetCommand(
        IReadOnlyList<ViewerKeyBinding> bindings,
        WindowKeyEvent input,
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

    internal static IReadOnlyList<ViewerCommandShortcut> GetShortcuts(ViewerCommandId command) =>
        [.. Window.Concat(Viewer)
            .Where(binding => binding.Command == command)
            .Select(binding => binding.Shortcut)];
}
