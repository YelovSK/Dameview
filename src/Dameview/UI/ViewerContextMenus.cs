using System.Drawing;
using Dameview.Commands;
using Dameview.Installation;
using Dameview.Navigation;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.Viewing;

namespace Dameview.UI;

/// <summary>Decides what each part of the viewer offers when right-clicked, and what the app menu offers.</summary>
/// <remarks>
/// Items are commands aimed at what was clicked, so they share their labels, availability, and
/// behavior with the command palette and shortcuts.
/// </remarks>
internal sealed class ViewerContextMenus
{
    private readonly PopupHost _popupHost;
    private readonly IAppActions _app;

    internal ViewerContextMenus(PopupHost popupHost, IAppActions app)
    {
        _popupHost = popupHost;
        _app = app;
    }

    internal ViewerKeyBindings KeyBindings { get; set; } = ViewerKeyBindings.Defaults;
    internal FolderSort DefaultSort { get; set; }

    /// <param name="point">Where the image panel was clicked, in its own coordinates.</param>
    internal void ShowForImage(ViewerTab tab, UiElement imagePanel, PointF point)
    {
        // The panel's pixels are its viewport's, so zooming centers where the menu was opened.
        float dpi = imagePanel.Root?.Dpi ?? UiDpi.Default;
        var context = CommandContext.For(
            tab,
            new PointF(UiDpi.DipsToPixels(point.X, dpi), UiDpi.DipsToPixels(point.Y, dpi)));
        Show(imagePanel, point,
        [
            Group(context, AppCommands.CopyImage, AppCommands.CopyFile, AppCommands.CopyFilePath),
            Group(context, AppCommands.OpenWith, AppCommands.ShowInFolder, AppCommands.ShowProperties),
            Group(context, AppCommands.FitImage, AppCommands.ShowActualSize),
            Group(context, AppCommands.RotateLeft, AppCommands.RotateRight),
            Group(context, AppCommands.SplitRight, AppCommands.SplitDown),
            [Item(AppCommands.DeleteFile, context, tone: UiButtonTone.Danger)],
            [AllCommandsItem(context)],
        ]);
    }

    internal void ShowForGalleryItem(string path, UiElement anchor, PointF point)
    {
        var context = CommandContext.ForFile(path);
        Show(anchor, point,
        [
            [new ContextMenuItem("Open in new tab", () => _app.OpenImageInNewTab(path))],
            Group(context, AppCommands.CopyImage, AppCommands.CopyFile, AppCommands.CopyFilePath),
            Group(context, AppCommands.OpenWith, AppCommands.ShowInFolder, AppCommands.ShowProperties),
            [Item(AppCommands.DeleteFile, context, tone: UiButtonTone.Danger)],
        ]);
    }

    internal void ShowForTab(ViewerTab tab, UiElement anchor, PointF point)
    {
        var context = CommandContext.For(tab);
        Show(anchor, point,
        [
            [
                Item(AppCommands.NewTab, context, label: "Duplicate tab"),
                Item(AppCommands.CloseTab, context),
                Item(AppCommands.ReopenClosedTab, context),
            ],
            Group(context, AppCommands.CopyFilePath, AppCommands.ShowInFolder),
            [AllCommandsItem(context)],
        ]);
    }

    internal void ShowSortMenu(ViewerTab tab, UiElement button)
    {
        FolderSort? current = tab.Session.State.SortOverride;
        ContextMenu.ShowBelow(
            _popupHost,
            button,
            [
                [SortItem(tab, null, $"Default ({SortLabel(DefaultSort)})", current is null)],
                [.. Enum.GetValues<FolderSort>().Select(sort => SortItem(tab, sort, SortLabel(sort), current == sort))],
            ]);
    }

    /// <summary>Opens the app menu below its button in the title bar.</summary>
    internal void ShowAppMenu(UiElement button)
    {
        CommandContext context = _app.ActiveContext;
        ContextMenu.ShowBelow(
            _popupHost,
            button,
            [
                Group(context, AppCommands.OpenFile),
                [
                    Item(AppCommands.ShowCommandPalette, context, label: "Commands"),
                    Item(AppCommands.ShowSettings, context),
                ],
            ],
            $"Dameview {AppInstallation.CurrentDisplayVersion}");
    }

    private static ContextMenuItem SortItem(ViewerTab tab, FolderSort? sort, string label, bool isChecked) =>
        new(label, () => tab.Session.SetSortOverride(sort), IsChecked: isChecked);

    private static string SortLabel(FolderSort sort) => sort switch
    {
        FolderSort.NameAscending => "Name, A–Z",
        FolderSort.NameDescending => "Name, Z–A",
        FolderSort.DateModifiedNewest => "Modified, newest",
        FolderSort.DateModifiedOldest => "Modified, oldest",
        FolderSort.DateCreatedNewest => "Created, newest",
        FolderSort.DateCreatedOldest => "Created, oldest",
        FolderSort.SizeLargest => "Size, largest",
        FolderSort.SizeSmallest => "Size, smallest",
        _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, null),
    };

    // The palette acts on the active tab whatever was clicked, so it is the one global item the
    // right-click menus offer, there for anyone who does not know its shortcut.
    private ContextMenuItem AllCommandsItem(CommandContext context) =>
        Item(AppCommands.ShowCommandPalette, context, label: "All commands…");

    private void Show(UiElement anchor, PointF point, IReadOnlyList<ContextMenuItem>[] groups) =>
        ContextMenu.Show(_popupHost, anchor, point, groups);

    private ContextMenuItem[] Group(CommandContext context, params Command[] commands) =>
        [.. commands.Select(command => Item(command, context))];

    private ContextMenuItem Item(
        Command command,
        CommandContext context,
        string? label = null,
        UiButtonTone tone = UiButtonTone.Default)
    {
        IReadOnlyList<ViewerCommandShortcut> shortcuts = KeyBindings.GetShortcuts(command);
        // Shortcuts act on the active tab, so they are only a fair hint when that was clicked.
        bool hintsApply = context.Tab is { } tab && tab == _app.ActiveContext.Tab;
        return new ContextMenuItem(
            label ?? command.Label,
            () => _app.Execute(command, context),
            hintsApply && shortcuts.Count > 0 ? shortcuts[0].Text : null,
            _app.CanExecute(command, context),
            tone);
    }
}
