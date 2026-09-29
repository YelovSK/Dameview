using Dameview.Viewing;

namespace Dameview.Commands;

/// <summary>Direct manipulation of the workspace, which has no name or shortcut to be a command.</summary>
internal interface IViewerActions
{
    /// <summary>
    /// Opens a path from outside the current folder, which rescans.
    /// </summary>
    public void OpenImage(string path);

    public void SelectImage(string path);

    public void OpenImageInNewTab(string path);

    public void OpenImageInNewTab(string path, WorkspaceDropTarget target);

    public bool MoveTab(ViewerPane sourcePane, ViewerTab tab, WorkspaceDropTarget target);

    public void SelectPane(ViewerPane pane);

    public void SelectTab(ViewerPane pane, int index);
}
