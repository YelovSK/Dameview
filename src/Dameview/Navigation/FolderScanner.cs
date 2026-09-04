namespace Dameview.Navigation;

internal interface IFolderScanner
{
    public Task<FolderEntry[]> ScanAsync(string directoryPath, CancellationToken cancellationToken);
}

internal sealed class FolderScanner(Func<string, bool> isProbablySupported) : IFolderScanner
{
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
