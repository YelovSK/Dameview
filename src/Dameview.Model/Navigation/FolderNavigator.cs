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

    /// <summary>
    /// Only files whose names contain this are listed and navigated to. The current file stays
    /// current even when it does not match.
    /// </summary>
    internal string NameFilter { get; set; } = string.Empty;

    internal FolderEntry? CurrentEntry => _currentIndex >= 0 ? _files[_currentIndex].Metadata : null;

    internal void Clear()
    {
        _files = [];
        _currentIndex = -1;
    }

    internal void SetFiles(IEnumerable<FolderEntry> files, string? currentPath)
    {
        _files = [.. files.Select(file => new NavigationEntry(file.FullName, file))];
        SortFiles(_files, Sort);
        SetCurrent(currentPath);
    }

    /// <summary>Adds files found after those already known, such as a later batch of a scan.</summary>
    internal void AddFiles(IEnumerable<FolderEntry> files)
    {
        NavigationEntry[] added = [.. files.Select(file => new NavigationEntry(file.FullName, file))];
        if (added.Length == 0)
        {
            return;
        }

        SortFiles(added, Sort);
        string? currentPath = _currentIndex >= 0 ? _files[_currentIndex].Path : null;
        NavigationEntry[] known = _files;

        // The current file stands in without metadata until the scan reaches it.
        if (currentPath is not null
            && _files[_currentIndex].Metadata is null
            && added.Any(file => _pathComparer.Equals(file.Path, currentPath)))
        {
            known = [.. _files.Where((_, index) => index != _currentIndex)];
        }

        _files = Merge(known, added, Sort);
        _currentIndex = currentPath is null ? -1 : IndexOf(currentPath);
    }

    internal FolderEntry[] GetFiles() =>
        [.. _files.Where(file => file.Metadata is not null && Matches(file)).Select(file => file.Metadata!)];

    /// <summary>
    /// The paths at these offsets from the current one, wrapping around, without the current
    /// path or repeats, which small folders would otherwise produce.
    /// </summary>
    internal List<string> GetRelativePaths(IEnumerable<int> offsets)
    {
        List<string> paths = [];
        // Each direction is walked once, going on from where the previous offset stopped, since
        // with a narrow filter every step can scan much of the folder.
        (int Distance, int Index) forward = (0, _currentIndex);
        (int Distance, int Index) backward = (0, _currentIndex);
        foreach (int offset in offsets)
        {
            ref (int Distance, int Index) walk = ref offset > 0 ? ref forward : ref backward;
            int distance = Math.Abs(offset);
            if (distance < walk.Distance)
            {
                walk = (0, _currentIndex);
            }

            while (walk.Distance < distance && walk.Index >= 0)
            {
                int next = FindMatch(walk.Index, Math.Sign(offset));
                // Back at the start, every further step only repeats.
                walk = (walk.Distance + 1, next == _currentIndex ? -1 : next);
            }

            if (walk.Distance == distance && walk.Index >= 0 && !paths.Contains(_files[walk.Index].Path))
            {
                paths.Add(_files[walk.Index].Path);
            }
        }

        return paths;
    }

    internal string? MoveToNextPath()
    {
        return MoveToRelativePath(1);
    }

    internal string? MoveToPreviousPath()
    {
        return MoveToRelativePath(-1);
    }

    /// <summary>Makes a file current, adding it when the folder lacks it, or makes none current.</summary>
    internal void SetCurrent(string? path)
    {
        if (path is null)
        {
            _currentIndex = -1;
            return;
        }

        string fullPath = Path.GetFullPath(path);
        _currentIndex = IndexOf(fullPath);

        if (_currentIndex < 0)
        {
            _files = [.. _files, new NavigationEntry(fullPath, null)];
            SortFiles(_files, Sort);
            _currentIndex = IndexOf(fullPath);
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
            _currentIndex = IndexOf(currentPath);
        }
    }

    private int IndexOf(string path) =>
        Array.FindIndex(_files, file => _pathComparer.Equals(file.Path, path));

    private string? MoveToRelativePath(int direction)
    {
        int index = FindMatch(_currentIndex, direction);
        if (index < 0)
        {
            return null;
        }

        _currentIndex = index;
        return _files[index].Path;
    }

    /// <summary>The nearest other matching file in a direction, wrapping around, or -1 when there is none.</summary>
    private int FindMatch(int from, int direction)
    {
        if (from < 0)
        {
            return -1;
        }

        for (int distance = 1; distance < _files.Length; distance++)
        {
            int index = (from + (direction * distance) + _files.Length) % _files.Length;
            if (Matches(_files[index]))
            {
                return index;
            }
        }

        return -1;
    }

    internal bool HasOtherMatch => FindMatch(_currentIndex, 1) >= 0;

    private bool Matches(NavigationEntry file) => file.Name.Contains(NameFilter, StringComparison.OrdinalIgnoreCase);

    private static void SortFiles(NavigationEntry[] files, FolderSort sort)
    {
        Array.Sort(files, (left, right) => Compare(left, right, sort));
    }

    private static NavigationEntry[] Merge(NavigationEntry[] left, NavigationEntry[] right, FolderSort sort)
    {
        var merged = new NavigationEntry[left.Length + right.Length];
        int leftIndex = 0;
        int rightIndex = 0;
        for (int index = 0; index < merged.Length; index++)
        {
            merged[index] = rightIndex == right.Length
                || (leftIndex < left.Length && Compare(left[leftIndex], right[rightIndex], sort) <= 0)
                ? left[leftIndex++]
                : right[rightIndex++];
        }

        return merged;
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
