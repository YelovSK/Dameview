using Dameview.Viewing;

namespace Dameview.Commands;

internal interface IViewerCommands
{
    // The parameterless overloads act on the active pane.
    public void ShowPreviousImage();

    public void ShowPreviousImage(ViewerPane pane);

    public void ShowNextImage();

    public void ShowNextImage(ViewerPane pane);

    public void FitImage();

    public void FitImage(ViewerPane pane);

    public void ShowActualSize();

    public void ShowActualSize(ViewerPane pane);

    public void SplitRight();

    public void SplitRight(ViewerPane pane);

    public void SplitDown();

    public void SplitDown(ViewerPane pane);

    // Opens a path from outside the current folder, which rescans.
    public void OpenImage(string path);

    public void SelectImage(string path);

    public void OpenImageInNewTab(string path);

    public void OpenImageInNewTab(string path, WorkspaceDropTarget target);

    public bool MoveTab(ViewerPane sourcePane, ViewerTab tab, WorkspaceDropTarget target);

    public void SelectPane(ViewerPane pane);

    public void DuplicateActiveTab(ViewerPane pane);

    public void SelectTab(ViewerPane pane, int index);

    public void CloseTab(ViewerPane pane, int index);
}
