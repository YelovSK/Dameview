using Dameview.Viewing;

namespace Dameview.Commands;

internal interface IViewerCommands
{
    public void ShowPreviousImage();

    public void ShowNextImage();

    public void FitImage();

    public void ShowActualSize();

    public void SplitRight();

    public void SplitDown();

    public void OpenImage(string path);

    public void OpenImageInNewTab(string path);

    public void OpenImageInNewTab(string path, WorkspaceDropTarget target);

    public bool MoveTab(ViewerPane sourcePane, ViewerTab tab, WorkspaceDropTarget target);

    public void SelectPane(ViewerPane pane);

    public void DuplicateActiveTab(ViewerPane pane);

    public void SelectTab(ViewerPane pane, int index);

    public void CloseTab(ViewerPane pane, int index);
}
