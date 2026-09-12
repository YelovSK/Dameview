namespace Dameview.Navigation;

internal interface IFolderWatcher : IDisposable
{
    public event Action<string>? Changed;
    public event Action<string>? Created;
    public event Action<string>? Deleted;
    public event Action<string, string>? Renamed;
    public event Action? Error;

    public void Start(string directoryPath);
    public void Stop();
}

internal sealed class FileSystemFolderWatcher : IFolderWatcher
{
    private FileSystemWatcher? _watcher;

    public event Action<string>? Changed;
    public event Action<string>? Created;
    public event Action<string>? Deleted;
    public event Action<string, string>? Renamed;
    public event Action? Error;

    public void Start(string directoryPath)
    {
        Stop();
        var watcher = new FileSystemWatcher(directoryPath)
        {
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size
                | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
        };
        watcher.Changed += (_, e) => Changed?.Invoke(e.FullPath);
        watcher.Created += (_, e) => Created?.Invoke(e.FullPath);
        watcher.Deleted += (_, e) => Deleted?.Invoke(e.FullPath);
        watcher.Renamed += (_, e) => Renamed?.Invoke(e.FullPath, e.OldFullPath);
        watcher.Error += (_, _) => Error?.Invoke();
        watcher.EnableRaisingEvents = true;
        _watcher = watcher;
    }

    public void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    public void Dispose() => Stop();
}
