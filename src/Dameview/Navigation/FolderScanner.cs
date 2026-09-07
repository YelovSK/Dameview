namespace Dameview.Navigation;

/// <summary>Finds supported image files in a directory without blocking the UI thread.</summary>
internal interface IFolderScanner
{
    /// <summary>Scans a directory and returns file metadata in an arbitrary enumeration order.</summary>
    public Task<FolderEntry[]> ScanAsync(string directoryPath, CancellationToken cancellationToken);

    /// <summary>Whether a path is a supported image file. Used to ignore unrelated directory events.</summary>
    public bool IsProbablySupported(string path);
}

internal sealed class FolderScanner(Func<string, bool> isProbablySupported) : IFolderScanner
{
    public bool IsProbablySupported(string path) => isProbablySupported(path);

    public Task<FolderEntry[]> ScanAsync(string directoryPath, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var files = new List<FolderEntry>();
            foreach (FileInfo file in new DirectoryInfo(directoryPath).EnumerateFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (isProbablySupported(file.FullName))
                {
                    files.Add(new FolderEntry(file.FullName, file.Length,
                        file.CreationTimeUtc, file.LastWriteTimeUtc));
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return files.ToArray();
        }, cancellationToken);
    }
}
