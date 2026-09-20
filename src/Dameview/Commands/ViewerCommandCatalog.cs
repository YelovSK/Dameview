namespace Dameview.Commands;

internal enum ViewerCommandId
{
    NewTab,
    CloseTab,
    ReopenClosedTab,
    PreviousTab,
    NextTab,
    PreviousImage,
    NextImage,
    FitImage,
    ShowActualSize,
    ToggleFitActualSize,
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

// Window commands run before the UI sees the key.
internal enum ViewerCommandScope
{
    Window,
    Viewer,
}

internal sealed record ViewerCommand(
    ViewerCommandId Id,
    string Label,
    ViewerCommandScope Scope);

internal static class ViewerCommandCatalog
{
    internal static IReadOnlyList<ViewerCommand> Commands { get; } =
    [
        new(ViewerCommandId.NewTab, "New tab", ViewerCommandScope.Window),
        new(ViewerCommandId.CloseTab, "Close tab", ViewerCommandScope.Window),
        new(ViewerCommandId.ReopenClosedTab, "Reopen closed tab", ViewerCommandScope.Window),
        new(ViewerCommandId.PreviousTab, "Select previous tab", ViewerCommandScope.Window),
        new(ViewerCommandId.NextTab, "Select next tab", ViewerCommandScope.Window),
        new(ViewerCommandId.PreviousImage, "Previous image", ViewerCommandScope.Viewer),
        new(ViewerCommandId.NextImage, "Next image", ViewerCommandScope.Viewer),
        new(ViewerCommandId.FitImage, "Fit image", ViewerCommandScope.Viewer),
        new(ViewerCommandId.ShowActualSize, "Show actual size", ViewerCommandScope.Viewer),
        new(
            ViewerCommandId.ToggleFitActualSize,
            "Toggle fit and actual size",
            ViewerCommandScope.Viewer),
        new(ViewerCommandId.CopyImage, "Copy image", ViewerCommandScope.Viewer),
        new(ViewerCommandId.ToggleFullscreen, "Toggle fullscreen", ViewerCommandScope.Window),
        new(ViewerCommandId.SplitRight, "Split right", ViewerCommandScope.Window),
        new(ViewerCommandId.SplitDown, "Split down", ViewerCommandScope.Window),
        new(ViewerCommandId.BalancePanes, "Balance pane layout", ViewerCommandScope.Window),
        new(ViewerCommandId.OptimizePaneLayout, "Optimize pane layout", ViewerCommandScope.Window),
        new(
            ViewerCommandId.TogglePerformanceOverlay,
            "Toggle performance overlay",
            ViewerCommandScope.Window),
        new(ViewerCommandId.ShowSettings, "Open settings", ViewerCommandScope.Window),
        new(ViewerCommandId.ShowCommandPalette, "Show command palette", ViewerCommandScope.Window),
    ];

    internal static ViewerCommandScope GetScope(ViewerCommandId command) =>
        Commands.First(candidate => candidate.Id == command).Scope;
}
