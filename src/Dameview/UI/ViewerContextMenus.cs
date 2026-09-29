using System.Drawing;
using Dameview.Commands;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.Viewing;

namespace Dameview.UI;

/// <summary>Decides what each part of the viewer offers when right-clicked, and opens it.</summary>
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
            Group(context, AppCommands.SplitRight, AppCommands.SplitDown),
            [Item(AppCommands.DeleteFile, context, tone: UiButtonTone.Danger)],
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
        ]);
    }

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
