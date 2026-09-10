using Dameview.Platform;
using Dameview.Platform.Networking;

namespace Dameview.Updates;

internal sealed class GitHubUpdateClient : IUpdateClient
{
    private const string Host = "github.com";
    private const string LatestReleasePath = "/YelovSK/Dameview/releases/latest";
    private const string ReleaseTagMarker = "/releases/tag/";
    private const long MaximumDownloadBytes = 64L * 1024L * 1024L;

    public AppRelease GetLatestRelease()
    {
        using var request = WinHttpRequest.Send(Host, "HEAD", LatestReleasePath);
        string finalUrl = request.GetFinalUrl();
        int marker = finalUrl.LastIndexOf(ReleaseTagMarker, StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            throw new InvalidDataException("GitHub did not redirect to a release tag.");
        }

        string tag = finalUrl[(marker + ReleaseTagMarker.Length)..].TrimEnd('/');
        return AppRelease.FromTag(tag);
    }

    public string Download(AppRelease release)
    {
        string updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Dameview",
            "Updates");
        Directory.CreateDirectory(updateDirectory);
        string downloadPath = Path.Combine(updateDirectory, "Dameview.update.download");
        string updatePath = Path.Combine(updateDirectory, "Dameview.update.exe");

        try
        {
            string releasePath = $"/YelovSK/Dameview/releases/download/{release.Tag}/Dameview.exe";
            using var request = WinHttpRequest.Send(Host, "GET", releasePath);
            using (var output = new FileStream(
                downloadPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                request.CopyResponseTo(output, MaximumDownloadBytes);
                output.Flush(flushToDisk: true);
            }

            Version downloadedVersion = AppInstallation.ReadVersion(downloadPath)
                ?? throw new InvalidDataException("The downloaded file has no valid version.");
            if (AppRelease.Normalize(downloadedVersion) != release.Version)
            {
                throw new InvalidDataException(
                    $"The downloaded version {downloadedVersion} does not match release {release.Tag}.");
            }

            File.Move(downloadPath, updatePath, overwrite: true);
            return updatePath;
        }
        catch
        {
            File.Delete(downloadPath);
            throw;
        }
    }
}
