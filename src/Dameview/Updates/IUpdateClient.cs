namespace Dameview.Updates;

internal sealed record UpdateFailure(string Message);

internal readonly union ReleaseResult(AppRelease, UpdateFailure);

/// <summary>The path of the downloaded executable, or why it could not be downloaded.</summary>
internal readonly union DownloadResult(string, UpdateFailure);

internal interface IUpdateClient
{
    public ReleaseResult GetLatestRelease();

    public DownloadResult Download(AppRelease release);
}
