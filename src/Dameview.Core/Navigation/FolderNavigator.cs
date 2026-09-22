namespace Dameview.Navigation;

internal sealed class FolderNavigator
{
    private static readonly StringComparer _pathComparer = StringComparer.OrdinalIgnoreCase;

    private NavigationEntry[] _files = [];
    private int _currentIndex = -1;

    internal FolderNavigator(FolderSort sort = FolderSort.NameAscending)
    {
        Sort = sort;
    }

    internal FolderSort Sort { get; private set; }

    internal FolderEntry? CurrentEntry => _currentIndex >= 0 ? _files[_currentIndex].Metadata : null;

    internal void Clear()
    {
        _files = [];
        _currentIndex = -1;
    }

    internal void SetFiles(IEnumerable<FolderEntry> files, string currentPath)
    {
        _files = [.. files.Select(file => new NavigationEntry(file.FullName, file))];
        SortFiles(_files, Sort);
        SetCurrent(currentPath);
    }

    internal FolderEntry[] GetFiles() =>
        [.. _files.Where(file => file.Metadata is not null).Select(file => file.Metadata!)];

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
            file => _pathComparer.Equals(file.Path, fullPath));

        if (_currentIndex < 0)
        {
            _files = [.. _files, new NavigationEntry(fullPath, null)];
            SortFiles(_files, Sort);
            _currentIndex = Array.FindIndex(
                _files,
                file => _pathComparer.Equals(file.Path, fullPath));
        }
    }

    internal void SetSort(FolderSort sort)
    {
        if (sort == Sort)
        {
            return;
        }

        string? currentPath = _currentIndex >= 0 ? _files[_currentIndex].Path : null;
        Sort = sort;
        SortFiles(_files, Sort);

        if (currentPath is not null)
        {
            _currentIndex = Array.FindIndex(
                _files,
                file => _pathComparer.Equals(file.Path, currentPath));
        }
    }

    private string? GetRelativePath(int offset)
    {
        if (_files.Length < 2 || _currentIndex < 0)
        {
            return null;
        }

        int index = (_currentIndex + offset + _files.Length) % _files.Length;
        return _files[index].Path;
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

    private static void SortFiles(NavigationEntry[] files, FolderSort sort)
    {
        Array.Sort(files, (left, right) => Compare(left, right, sort));
    }

    private static int Compare(NavigationEntry left, NavigationEntry right, FolderSort sort)
    {
        if (sort is not FolderSort.NameAscending and not FolderSort.NameDescending
            && (left.Metadata is null || right.Metadata is null))
        {
            return left.Metadata is null
                ? right.Metadata is null ? _pathComparer.Compare(left.Path, right.Path) : 1
                : -1;
        }

        int result = sort switch
        {
            FolderSort.NameAscending => _pathComparer.Compare(left.Name, right.Name),
            FolderSort.NameDescending => _pathComparer.Compare(right.Name, left.Name),
            FolderSort.DateModifiedNewest => right.Metadata!.LastWriteTimeUtc.CompareTo(left.Metadata!.LastWriteTimeUtc),
            FolderSort.DateModifiedOldest => left.Metadata!.LastWriteTimeUtc.CompareTo(right.Metadata!.LastWriteTimeUtc),
            FolderSort.DateCreatedNewest => right.Metadata!.CreationTimeUtc.CompareTo(left.Metadata!.CreationTimeUtc),
            FolderSort.DateCreatedOldest => left.Metadata!.CreationTimeUtc.CompareTo(right.Metadata!.CreationTimeUtc),
            FolderSort.SizeLargest => right.Metadata!.Length.CompareTo(left.Metadata!.Length),
            FolderSort.SizeSmallest => left.Metadata!.Length.CompareTo(right.Metadata!.Length),
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, null),
        };

        return result != 0
            ? result
            : _pathComparer.Compare(left.Path, right.Path);
    }

    private readonly record struct NavigationEntry(string Path, FolderEntry? Metadata)
    {
        internal string Name { get; } = System.IO.Path.GetFileName(Path);
    }
}
