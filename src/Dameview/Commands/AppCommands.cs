using System.Diagnostics;
using Dameview.Imaging;
using Dameview.Settings;
using Dameview.Viewing;
using Dameview.Win32.Input;

namespace Dameview.Commands;

/// <summary>Every command, each defined whole: its name, shortcuts, and what it does.</summary>
internal static class AppCommands
{
    internal static readonly Command OpenFile = Command.Global(
        "openFile", "Open image…", CommandScope.Window,
        host => host.OpenPickedFile(),
        shortcuts: [new(WindowKey.O, Control: true)]);

    internal static readonly Command NewTab = Command.ForTab(
        "newTab", "New tab", CommandScope.Window,
        (host, tab) => host.Workspace.DuplicateTab(tab),
        shortcuts: [new(WindowKey.T, Control: true)]);

    internal static readonly Command CloseTab = Command.ForTab(
        "closeTab", "Close tab", CommandScope.Window,
        (host, tab) => host.CloseTab(tab),
        shortcuts: [new(WindowKey.W, Control: true)]);

    internal static readonly Command ReopenClosedTab = Command.Global(
        "reopenClosedTab", "Reopen closed tab", CommandScope.Window,
        host => host.Workspace.ReopenClosedTab(),
        host => host.Workspace.HasClosedTabs,
        [new(WindowKey.T, Control: true, Shift: true)]);

    internal static readonly Command PreviousTab = Command.Global(
        "previousTab", "Select previous tab", CommandScope.Window,
        host => host.Workspace.SelectRelativeTab(-1),
        host => host.Workspace.Count > 1,
        [new(WindowKey.Tab, Control: true, Shift: true)]);

    internal static readonly Command NextTab = Command.Global(
        "nextTab", "Select next tab", CommandScope.Window,
        host => host.Workspace.SelectRelativeTab(1),
        host => host.Workspace.Count > 1,
        [new(WindowKey.Tab, Control: true)]);

    internal static readonly Command PreviousImage = Command.ForTab(
        "previousImage", "Previous image", CommandScope.Viewer,
        (host, tab) =>
        {
            tab.Session.ShowPreviousImage();
            host.Ui.CenterGallerySelection();
        },
        HasOtherImages,
        [new(WindowKey.Left)]);

    internal static readonly Command NextImage = Command.ForTab(
        "nextImage", "Next image", CommandScope.Viewer,
        (host, tab) =>
        {
            tab.Session.ShowNextImage();
            host.Ui.CenterGallerySelection();
        },
        HasOtherImages,
        [new(WindowKey.Right)]);

    internal static readonly Command FitImage = Command.ForTab(
        "fitImage", "Fit image", CommandScope.Viewer,
        (_, tab) => tab.Session.Animator.Fit(),
        ShowsImage,
        [new(WindowKey.F)]);

    internal static readonly Command ShowActualSize = Command.ForTab(
        "showActualSize", "Show actual size", CommandScope.Viewer,
        (_, tab, anchor) => tab.Session.Animator.ShowActualSizeAt(anchor),
        ShowsImage,
        [new(WindowKey.Number1), new(WindowKey.Numpad1)]);

    internal static readonly Command ToggleFitActualSize = Command.ForTab(
        "toggleFitActualSize", "Toggle fit and actual size", CommandScope.Viewer,
        (_, tab, anchor) => tab.Session.Animator.ToggleFitAndActualSizeAt(anchor),
        ShowsImage,
        [new(WindowKey.Z)]);

    internal static readonly Command RotateLeft = ForOrientation(
        "rotateLeft", "Rotate left",
        static orientation => orientation.RotateCounterclockwise(),
        new(WindowKey.L));

    internal static readonly Command RotateRight = ForOrientation(
        "rotateRight", "Rotate right",
        static orientation => orientation.RotateClockwise(),
        new(WindowKey.R));

    internal static readonly Command FlipHorizontal = ForOrientation(
        "flipHorizontal", "Flip horizontally",
        static orientation => orientation.FlipHorizontal(),
        new(WindowKey.H));

    internal static readonly Command FlipVertical = ForOrientation(
        "flipVertical", "Flip vertically",
        static orientation => orientation.FlipVertical(),
        new(WindowKey.V));

    internal static readonly Command CopyImage = Command.ForImage(
        "copyImage", "Copy image", CommandScope.Viewer,
        (host, path) => host.Files.CopyImage(path),
        [new(WindowKey.C, Control: true)]);

    internal static readonly Command CopyFilePath = Command.ForImage(
        "copyFilePath", "Copy file path", CommandScope.Viewer,
        (host, path) => host.Files.CopyPath(path),
        [new(WindowKey.C, Control: true, Shift: true)]);

    internal static readonly Command CopyFile = Command.ForImage(
        "copyFile", "Copy file", CommandScope.Viewer,
        (host, path) => host.Files.CopyFile(path));

    internal static readonly Command ShowInFolder = Command.ForImage(
        "showInFolder", "Show in folder", CommandScope.Viewer,
        (host, path) => host.Files.ShowInFolder(path));

    internal static readonly Command OpenWith = Command.ForImage(
        "openWith", "Open with…", CommandScope.Viewer,
        (host, path) => host.Files.OpenWith(path));

    internal static readonly Command ShowProperties = Command.ForImage(
        "showProperties", "Show file properties", CommandScope.Viewer,
        (host, path) => host.Files.ShowProperties(path));

    internal static readonly Command DeleteFile = Command.ForImage(
        "deleteFile", "Delete file", CommandScope.Viewer,
        (host, path) => host.Files.MoveToRecycleBin(path),
        [new(WindowKey.Delete, Control: true)]);

    internal static readonly Command ToggleFullscreen = Command.Global(
        "toggleFullscreen", "Toggle fullscreen", CommandScope.Window,
        host => host.ToggleFullscreen(),
        shortcuts: [new(WindowKey.F11), new(WindowKey.F, Control: true)]);

    internal static readonly Command ToggleGallery = Command.Global(
        "toggleGallery", "Toggle gallery", CommandScope.Window,
        host => host.UpdateSettings(settings => settings with { GalleryEnabled = !settings.GalleryEnabled }),
        shortcuts: [new(WindowKey.G, Control: true)]);

    internal static readonly Command ToggleFlattenFolder = Command.ForTab(
        "toggleFlattenFolder", "Toggle flatten folder", CommandScope.Viewer,
        (_, tab) => tab.Session.ToggleFlattenFolder());

    internal static readonly Command SplitRight = Command.ForTab(
        "splitRight", "Split right", CommandScope.Window,
        (host, tab) => Split(host, tab, WorkspaceSplitOrientation.Horizontal),
        shortcuts: [new(WindowKey.S, Control: true, Shift: true)]);

    internal static readonly Command SplitDown = Command.ForTab(
        "splitDown", "Split down", CommandScope.Window,
        (host, tab) => Split(host, tab, WorkspaceSplitOrientation.Vertical),
        shortcuts: [new(WindowKey.S, Control: true)]);

    internal static readonly Command BalancePanes = Command.Global(
        "balancePanes", "Balance pane layout", CommandScope.Window,
        host => host.Workspace.BalancePanes(),
        host => host.Workspace.IsSplit);

    internal static readonly Command OptimizePaneLayout = Command.Global(
        "optimizePaneLayout", "Optimize pane layout", CommandScope.Window,
        host =>
        {
            if (!host.Ui.IsClosingPane)
            {
                host.Workspace.OptimizePaneLayout(host.Ui.PaneLayoutArea);
            }
        },
        host => host.Workspace.IsSplit);

    internal static readonly Command TogglePerformanceOverlay = Command.Global(
        "togglePerformanceOverlay", "Toggle performance overlay", CommandScope.Window,
        host => host.TogglePerformanceOverlay(),
        shortcuts: [new(WindowKey.F3)]);

    internal static readonly Command ShowSettings = Command.Global(
        "showSettings", "Open settings", CommandScope.Window,
        host => host.Ui.ShowSettings(),
        shortcuts: [new(WindowKey.Comma, Control: true)]);

    internal static readonly Command OpenDataFolder = Command.Global(
        "openDataFolder", "Open settings and logs folder", CommandScope.Window,
        _ => Process.Start(new ProcessStartInfo(Path.GetDirectoryName(SettingsService.DefaultPath)!)
        {
            UseShellExecute = true,
        })?.Dispose());

    internal static readonly Command ShowCommandPalette = Command.Global(
        "showCommandPalette", "Show command palette", CommandScope.Window,
        host => host.Ui.ShowCommandPalette(),
        shortcuts: [new(WindowKey.P, Control: true, Shift: true)]);

    /// <summary>In the order the command palette lists them.</summary>
    internal static IReadOnlyList<Command> All { get; } =
    [
        OpenFile,
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
        RotateLeft,
        RotateRight,
        FlipHorizontal,
        FlipVertical,
        CopyImage,
        CopyFilePath,
        CopyFile,
        ShowInFolder,
        OpenWith,
        ShowProperties,
        DeleteFile,
        ToggleFullscreen,
        ToggleGallery,
        ToggleFlattenFolder,
        SplitRight,
        SplitDown,
        BalancePanes,
        OptimizePaneLayout,
        TogglePerformanceOverlay,
        ShowSettings,
        OpenDataFolder,
        ShowCommandPalette,
    ];

    private static bool HasOtherImages(ViewerTab tab) => tab.Session.State.FolderEntries.Length > 1;

    private static bool ShowsImage(ViewerTab tab) => tab.Session.State.DisplayedImage is not null;

    private static Command ForOrientation(
        string id,
        string label,
        Func<ImageOrientation, ImageOrientation> change,
        ViewerCommandShortcut shortcut) =>
        Command.ForTab(
            id, label, CommandScope.Viewer,
            (_, tab) => tab.Session.ChangeOrientation(change),
            ShowsImage,
            [shortcut]);

    private static void Split(ICommandHost host, ViewerTab tab, WorkspaceSplitOrientation orientation)
    {
        if (!host.Ui.IsClosingPane)
        {
            host.Workspace.SplitPane(host.Workspace.PaneOf(tab), orientation);
        }
    }
}
