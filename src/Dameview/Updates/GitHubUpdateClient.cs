using Dameview.Installation;
using Dameview.Win32.Networking;

namespace Dameview.Updates;

internal sealed class GitHubUpdateClient : IUpdateClient
{
    private const string Host = "github.com";
    private const string UserAgent = "Dameview";
    private const string LatestReleasePath = "/YelovSK/Dameview/releases/latest";
    private const string ReleaseTagMarker = "/releases/tag/";
    private const long MaximumDownloadBytes = 64L * 1024L * 1024L;

    public ReleaseResult GetLatestRelease() => WinHttpRequest.Send(Host, "HEAD", LatestReleasePath, UserAgent) switch
    {
        HttpError error => new UpdateFailure(error.Message),
        WinHttpRequest request => ReadRelease(request),
    };

    public DownloadResult Download(AppRelease release)
    {
        string updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Dameview",
            "Updates");
        Directory.CreateDirectory(updateDirectory);
        string downloadPath = Path.Combine(updateDirectory, "Dameview.update.download");
        string updatePath = Path.Combine(updateDirectory, "Dameview.update.exe");

        VerifiedDownload verified;
        try
        {
            verified = DownloadTo(downloadPath, release);
        }
        catch
        {
            File.Delete(downloadPath);
            throw;
        }

        if (verified is UpdateFailure failure)
        {
            File.Delete(downloadPath);
            return failure;
        }

        File.Move(downloadPath, updatePath, overwrite: true);
        return updatePath;
    }

    private static ReleaseResult ReadRelease(WinHttpRequest request)
    {
        using (request)
        {
            return request.GetFinalUrl() switch
            {
                HttpError error => new UpdateFailure(error.Message),
                string finalUrl => ReadRelease(finalUrl),
            };
        }
    }

    private static ReleaseResult ReadRelease(string finalUrl)
    {
        int marker = finalUrl.LastIndexOf(ReleaseTagMarker, StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return new UpdateFailure("GitHub did not redirect to a release tag.");
        }

        string tag = finalUrl[(marker + ReleaseTagMarker.Length)..].TrimEnd('/');
        return AppRelease.FromTag(tag) is { } release
            ? release
            : new UpdateFailure($"The release tag '{tag}' is not a version.");
    }

    private static VerifiedDownload DownloadTo(string downloadPath, AppRelease release)
    {
        string releasePath = $"/YelovSK/Dameview/releases/download/{release.Tag}/Dameview.exe";
        HttpCopyResult copied = WinHttpRequest.Send(Host, "GET", releasePath, UserAgent) switch
        {
            HttpError error => error,
            WinHttpRequest request => Save(request, downloadPath),
        };
        if (copied is HttpError httpError)
        {
            return new UpdateFailure(httpError.Message);
        }

        Version? downloadedVersion = AppInstallation.ReadVersion(downloadPath);
        if (downloadedVersion is null)
        {
            return new UpdateFailure("The downloaded file has no valid version.");
        }

        if (AppRelease.Normalize(downloadedVersion) != release.Version)
        {
            return new UpdateFailure(
                $"The downloaded version {downloadedVersion} does not match release {release.Tag}.");
        }

        return downloadedVersion;
    }

    private static HttpCopyResult Save(WinHttpRequest request, string path)
    {
        using (request)
        using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            HttpCopyResult copied = request.CopyResponseTo(output, MaximumDownloadBytes);
            output.Flush(flushToDisk: true);
            return copied;
        }
    }

    /// <summary>The version the downloaded file turned out to be, or why it can't be used.</summary>
    private readonly union VerifiedDownload(Version, UpdateFailure);
}
