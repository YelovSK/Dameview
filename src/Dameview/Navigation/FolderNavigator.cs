namespace Dameview.Navigation;

internal sealed class FolderNavigator
{
    private static readonly StringComparer _pathComparer = StringComparer.OrdinalIgnoreCase;

    private FolderEntry[] _files = [];
    private int _currentIndex = -1;

    internal FolderNavigator(FolderSort sort = FolderSort.NameAscending)
    {
        Sort = sort;
    }

    internal void Clear()
    {
        _files = [];
        _currentIndex = -1;
    }

    internal void SetFiles(IEnumerable<FolderEntry> files, string currentPath)
    {
        _files = files.ToArray();
        SortFiles(_files, Sort);
        SetCurrent(currentPath);
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
        _currentIndex = Array.FindIndex(
            _files,
            file => _pathComparer.Equals(file.FullName, fullPath));

        if (_currentIndex < 0)
        {
            _files = [.. _files, new FolderEntry(fullPath, 0, default, default)];
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

    private static void SortFiles(FolderEntry[] files, FolderSort sort)
    {
        Array.Sort(files, (left, right) => Compare(left, right, sort));
    }

    private static int Compare(FolderEntry left, FolderEntry right, FolderSort sort)
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
