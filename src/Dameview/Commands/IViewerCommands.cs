namespace Dameview.Commands;

internal interface IViewerCommands
{
    public void ShowPreviousImage();

    public void ShowNextImage();

    public void FitImage();

    public void ShowActualSize();

    public void OpenImage(string path);

    public void OpenImageInNewTab(string path);

    public void SelectTab(int index);

    public void CloseTab(int index);
}
