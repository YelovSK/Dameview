namespace Dameview.Settings;

internal static class AtomicFile
{
    /// <summary>Replaces the file in one step, so a crash mid-write never leaves it half written.</summary>
    internal static void WriteAllText(string path, string text)
    {
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, text);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
