namespace Dameview.Navigation;

internal sealed class FolderNavigator
{
    private static readonly StringComparer _pathComparer = StringComparer.OrdinalIgnoreCase;

    private readonly Func<string, bool> _isProbablySupported;
    private FileInfo[] _files = [];
    private string? _directoryPath;
    private int _currentIndex = -1;

    internal FolderNavigator(
        Func<string, bool> isProbablySupported,
        FolderSort sort = FolderSort.NameAscending)
    {
        _isProbablySupported = isProbablySupported;
        Sort = sort;
    }

    internal FolderSort Sort { get; private set; }

    internal string? GetNextPath()
    {
        return GetRelativePath(1);
    }

    internal string? GetPreviousPath()
    {
        return GetRelativePath(-1);
    }

    internal string? MoveToNextPath()
    {
        return MoveToRelativePath(1);
    }

    internal string? MoveToPreviousPath()
    {
        return MoveToRelativePath(-1);
    }

    internal void SetCurrent(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string directoryPath = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The image path has no containing directory.", nameof(path));

        if (!_pathComparer.Equals(directoryPath, _directoryPath))
        {
            LoadDirectory(directoryPath);
        }

        _currentIndex = Array.FindIndex(
            _files,
            file => _pathComparer.Equals(file.FullName, fullPath));

        if (_currentIndex < 0)
        {
            _files = [.. _files, new FileInfo(fullPath)];
            SortFiles(_files, Sort);
            _currentIndex = Array.FindIndex(
                _files,
                file => _pathComparer.Equals(file.FullName, fullPath));
        }
    }

    internal void SetSort(FolderSort sort)
    {
        if (sort == Sort)
        {
            return;
        }

        string? currentPath = _currentIndex >= 0 ? _files[_currentIndex].FullName : null;
        Sort = sort;
        SortFiles(_files, Sort);

        if (currentPath is not null)
        {
            _currentIndex = Array.FindIndex(
                _files,
                file => _pathComparer.Equals(file.FullName, currentPath));
        }
    }

    private string? GetRelativePath(int offset)
    {
        if (_files.Length < 2 || _currentIndex < 0)
        {
            return null;
        }

        int index = (_currentIndex + offset + _files.Length) % _files.Length;
        return _files[index].FullName;
    }

    private string? MoveToRelativePath(int offset)
    {
        string? path = GetRelativePath(offset);
        if (path is not null)
        {
            _currentIndex = (_currentIndex + offset + _files.Length) % _files.Length;
        }

        return path;
    }

    private void LoadDirectory(string directoryPath)
    {
        FileInfo[] directoryFiles = new DirectoryInfo(directoryPath).GetFiles();
        _files = directoryFiles
            .Where(file => _isProbablySupported(file.FullName))
            .ToArray();

        SortFiles(_files, Sort);
        _directoryPath = directoryPath;
        _currentIndex = -1;
    }

    private static void SortFiles(FileInfo[] files, FolderSort sort)
    {
        Array.Sort(files, (left, right) => Compare(left, right, sort));
    }

    private static int Compare(FileInfo left, FileInfo right, FolderSort sort)
    {
        int result = sort switch
        {
            FolderSort.NameAscending => _pathComparer.Compare(left.Name, right.Name),
            FolderSort.NameDescending => _pathComparer.Compare(right.Name, left.Name),
            FolderSort.DateModifiedNewest => right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc),
            FolderSort.DateModifiedOldest => left.LastWriteTimeUtc.CompareTo(right.LastWriteTimeUtc),
            FolderSort.DateCreatedNewest => right.CreationTimeUtc.CompareTo(left.CreationTimeUtc),
            FolderSort.DateCreatedOldest => left.CreationTimeUtc.CompareTo(right.CreationTimeUtc),
            FolderSort.SizeLargest => right.Length.CompareTo(left.Length),
            FolderSort.SizeSmallest => left.Length.CompareTo(right.Length),
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, null),
        };

        return result != 0
            ? result
            : _pathComparer.Compare(left.FullName, right.FullName);
    }
}
