using System.IO.Enumeration;

namespace Dameview.Navigation;

/// <summary>Finds supported image files in a directory.</summary>
internal interface IFolderScanner
{
    /// <summary>
    /// Enumerates file metadata in an arbitrary order. Only enumerating does I/O, so the caller
    /// decides which thread it runs on.
    /// </summary>
    public IEnumerable<FolderEntry> Scan(FolderScope scope, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a scan of the scope would find a file at this path, judged by its name and the
    /// folders it is in. Used to ignore unrelated directory events.
    /// </summary>
    public bool WouldInclude(FolderScope scope, string path);
}

// Takes a span so that the files of a large tree that are not images cost no allocation.
internal sealed class FolderScanner(Func<ReadOnlySpan<char>, bool> isProbablySupported) : IFolderScanner
{
    // Hidden and system folders hold things like the recycle bin and version control internals,
    // and following links into other trees could loop.
    private const FileAttributes SkippedFolderAttributes =
        FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint;

    public bool WouldInclude(FolderScope scope, string path)
    {
        if (!isProbablySupported(path))
        {
            return false;
        }

        // The scope's own folder is scanned whatever its attributes, so only those below it count.
        for (string? folder = Path.GetDirectoryName(path);
            folder is not null && folder.Length > scope.DirectoryPath.Length;
            folder = Path.GetDirectoryName(folder))
        {
            if (IsSkippedFolder(folder))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSkippedFolder(FileAttributes attributes) =>
        (attributes & SkippedFolderAttributes) != 0;

    // A folder that is already gone cannot be judged, and a rescan is the safe answer.
    private static bool IsSkippedFolder(string path)
    {
        try
        {
            return IsSkippedFolder(File.GetAttributes(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public IEnumerable<FolderEntry> Scan(FolderScope scope, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = scope.Recursive,
            // One unreadable subfolder must not hide the rest of a tree.
            IgnoreInaccessible = scope.Recursive,
            AttributesToSkip = 0,
            // Fewer directory queries per folder than the default.
            BufferSize = 64 * 1024,
        };
        var entries = new FileSystemEnumerable<FolderEntry>(
            scope.DirectoryPath,
            (ref entry) => new FolderEntry(
                entry.ToFullPath(),
                entry.Length,
                entry.CreationTimeUtc.UtcDateTime,
                entry.LastWriteTimeUtc.UtcDateTime),
            options)
        {
            ShouldIncludePredicate = (ref entry) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return !entry.IsDirectory && isProbablySupported(entry.FileName);
            },
            ShouldRecursePredicate = (ref entry) => !IsSkippedFolder(entry.Attributes),
        };
        foreach (FolderEntry entry in entries)
        {
            yield return entry;
        }
    }
}
