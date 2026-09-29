namespace Dameview.Navigation;

/// <summary>The folder whose images a session navigates, optionally including every subfolder.</summary>
internal sealed record FolderScope(string DirectoryPath, bool Recursive)
{
    internal bool Contains(string filePath)
    {
        string? parent = Path.GetDirectoryName(filePath);
        if (parent is null)
        {
            return false;
        }

        if (!Recursive)
        {
            return string.Equals(parent, DirectoryPath, StringComparison.OrdinalIgnoreCase);
        }

        return parent.StartsWith(DirectoryPath, StringComparison.OrdinalIgnoreCase)
            && (parent.Length == DirectoryPath.Length
                || Path.EndsInDirectorySeparator(DirectoryPath)
                || parent[DirectoryPath.Length] == Path.DirectorySeparatorChar);
    }

    public bool Equals(FolderScope? other) =>
        other is not null
        && Recursive == other.Recursive
        && string.Equals(DirectoryPath, other.DirectoryPath, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(DirectoryPath), Recursive);
}
