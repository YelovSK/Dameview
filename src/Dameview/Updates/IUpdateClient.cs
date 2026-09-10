namespace Dameview.Updates;

internal interface IUpdateClient
{
    public AppRelease GetLatestRelease();

    public string Download(AppRelease release);
}
