namespace Dameview.Navigation;

internal sealed record FolderEntry(
    string FullName,
    long Length,
    DateTime CreationTimeUtc,
    DateTime LastWriteTimeUtc)
{
    internal string Name => Path.GetFileName(FullName);
}
