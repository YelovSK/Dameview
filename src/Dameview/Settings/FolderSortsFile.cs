using Dameview.Diagnostics;
using Dameview.Navigation;

namespace Dameview.Settings;

internal static class FolderSortsFile
{
    private static string DefaultPath => Path.Combine(
        Path.GetDirectoryName(SettingsService.DefaultPath)!, "folder-sorts.txt");

    internal static FolderSortMemory Load()
    {
        try
        {
            return File.Exists(DefaultPath)
                ? FolderSortMemory.Read(File.ReadAllText(DefaultPath))
                : new FolderSortMemory();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("Settings", "Could not load folder sorts.", exception);
            return new FolderSortMemory();
        }
    }

    internal static void Save(FolderSortMemory memory)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DefaultPath)!);
            AtomicFile.WriteAllText(DefaultPath, memory.Write());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("Settings", "Could not save folder sorts.", exception);
        }
    }
}
