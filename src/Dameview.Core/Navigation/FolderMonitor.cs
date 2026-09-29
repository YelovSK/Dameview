namespace Dameview.Navigation;

internal interface IFolderMonitor : IDisposable
{
    public event Action<FolderUpdate>? Updated;

    public FolderScope? Scope { get; }

    public void Open(FolderScope scope);
    public void Close();
}

/// <summary>The folder a session shows, shared with other sessions showing the same scope.</summary>
internal sealed class FolderMonitor(FolderSources sources) : IFolderMonitor
{
    private IDisposable? _subscription;

    public event Action<FolderUpdate>? Updated;

    public FolderScope? Scope { get; private set; }

    // Subscribing before letting go keeps a scope that is opened again from being scanned again.
    public void Open(FolderScope scope)
    {
        IDisposable? previous = _subscription;
        _subscription = sources.Subscribe(scope, update => Updated?.Invoke(update));
        Scope = scope;
        previous?.Dispose();
    }

    public void Close()
    {
        _subscription?.Dispose();
        _subscription = null;
        Scope = null;
    }

    public void Dispose() => Close();
}
