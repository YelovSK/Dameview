using System.Text;

namespace Dameview.Navigation;

/// <summary>
/// The sorts picked for folders, keeping the most recently used. A sort can also apply to
/// subfolders, which use it unless they have one of their own.
/// </summary>
// Owner-thread confined.
internal sealed class FolderSortMemory
{
    private const int Limit = 1000;
    private const string FolderOnly = "folder";
    private const string WithSubfolders = "subfolders";

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Entry>.AlternateLookup<ReadOnlySpan<char>> _lookup;
    private long _lastUse;

    internal FolderSortMemory()
    {
        _lookup = _entries.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>Raised when a folder's sort is picked or forgotten.</summary>
    internal event Action? Changed;

    /// <summary>Finds the folder's own sort and the one it would inherit, and counts both as used.</summary>
    internal FolderSortChoice Resolve(string folderPath)
    {
        FolderSort? own = null;
        if (_entries.TryGetValue(folderPath, out Entry? ownEntry))
        {
            ownEntry.LastUse = ++_lastUse;
            own = ownEntry.Sort;
        }

        return new FolderSortChoice(own, FindInherited(folderPath));
    }

    private InheritedFolderSort? FindInherited(string folderPath)
    {
        for (ReadOnlySpan<char> parent = Path.GetDirectoryName(folderPath.AsSpan());
             !parent.IsEmpty;
             parent = Path.GetDirectoryName(parent))
        {
            if (_lookup.TryGetValue(parent, out string? path, out Entry? entry) && entry.IncludesSubfolders)
            {
                entry.LastUse = ++_lastUse;
                return new InheritedFolderSort(entry.Sort, path);
            }
        }

        return null;
    }

    /// <param name="sort">Null forgets the folder's own sort.</param>
    /// <param name="includesSubfolders">Whether subfolders without a sort of their own use it too.</param>
    internal void Set(string folderPath, FolderSort? sort, bool includesSubfolders)
    {
        if (sort is { } value)
        {
            _entries[folderPath] = new Entry(value, includesSubfolders) { LastUse = ++_lastUse };
            if (_entries.Count > Limit)
            {
                _entries.Remove(_entries.MinBy(pair => pair.Value.LastUse).Key);
            }
        }
        else if (!_entries.Remove(folderPath))
        {
            return;
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// One line per folder, least recently used first, holding the sort, whether subfolders use
    /// it, and the folder path, separated by tabs.
    /// </summary>
    internal string Write()
    {
        var text = new StringBuilder();
        foreach ((string path, Entry entry) in _entries.OrderBy(pair => pair.Value.LastUse))
        {
            text.Append(entry.Sort)
                .Append('\t')
                .Append(entry.IncludesSubfolders ? WithSubfolders : FolderOnly)
                .Append('\t')
                .Append(path)
                .Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Reads what <see cref="Write"/> wrote, skipping lines it cannot read.</summary>
    internal static FolderSortMemory Read(string text)
    {
        var memory = new FolderSortMemory();
        foreach (string line in text.Split('\n'))
        {
            if (line.TrimEnd('\r').Split('\t') is [string sortText, string scope and (FolderOnly or WithSubfolders), string path]
                && path.Length > 0
                && Enum.TryParse(sortText, out FolderSort sort)
                && Enum.IsDefined(sort))
            {
                memory._entries[path] = new Entry(sort, scope == WithSubfolders) { LastUse = ++memory._lastUse };
            }
        }

        return memory;
    }

    private sealed record Entry(FolderSort Sort, bool IncludesSubfolders)
    {
        internal long LastUse { get; set; }
    }
}

/// <param name="Own">The sort picked for the folder itself.</param>
/// <param name="Inherited">The sort of the nearest parent folder whose sort applies to its subfolders.</param>
internal readonly record struct FolderSortChoice(FolderSort? Own, InheritedFolderSort? Inherited)
{
    internal FolderSort? Sort => Own ?? Inherited?.Sort;
}

internal readonly record struct InheritedFolderSort(FolderSort Sort, string FolderPath);
