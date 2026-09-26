namespace Dameview.Navigation;

internal sealed record FolderUpdate(
    FolderEntry[] Entries,
    string? Error);

internal interface IFolderMonitor : IDisposable
{
    public event Action<FolderUpdate>? Updated;

    public string? CurrentDirectory { get; }

    public void Open(string directoryPath);
    public void Close();
}

internal sealed class FolderMonitor : IFolderMonitor
{
    private const int DefaultDebounceMilliseconds = 150;

    private readonly IFolderScanner _scanner;
    private readonly IFolderWatcher _watcher;
    private readonly SynchronizationContext _ownerContext;
    private readonly int _debounceMilliseconds;
    private readonly Lock _gate = new();
    private readonly Timer _debounceTimer;
    private Task _watcherTask = Task.CompletedTask;
    private CancellationTokenSource? _scanCts;
    private int _watcherVersion;
    private bool _disposed;

    internal FolderMonitor(
        IFolderScanner scanner,
        IFolderWatcher watcher,
        SynchronizationContext ownerContext,
        int debounceMilliseconds = DefaultDebounceMilliseconds)
    {
        _scanner = scanner;
        _watcher = watcher;
        _ownerContext = ownerContext;
        _debounceMilliseconds = debounceMilliseconds;
        _debounceTimer = new Timer(_ => HandleDebounceTimer(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher.Changed += ScheduleDebounceIfSupported;
        _watcher.Created += ScheduleDebounceIfSupported;
        _watcher.Deleted += ScheduleDebounceIfSupported;
        _watcher.Renamed += ScheduleDebounceIfSupported;
        _watcher.Error += ScheduleDebounce;
    }

    public event Action<FolderUpdate>? Updated;
    internal event Action<Exception>? WatcherFailed;

    public string? CurrentDirectory { get; private set; }

    public void Open(string directoryPath)
    {
        CancellationTokenSource cts;
        Task watcherReady;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CurrentDirectory = directoryPath;
            _scanCts?.Cancel();
            _scanCts?.Dispose();
            cts = new CancellationTokenSource();
            _scanCts = cts;
            _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            watcherReady = QueueWatcherReset(directoryPath);
        }

        _ = ScanAfterWatcherAsync(directoryPath, watcherReady, cts.Token);
    }

    public void Close()
    {
        lock (_gate)
        {
            CurrentDirectory = null;
            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = null;
            _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            QueueWatcherReset(null);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentDirectory = null;
            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = null;
            _debounceTimer.Dispose();
            QueueWatcherReset(null, dispose: true);
        }
    }

    // Watcher operations stay ordered without making the owner thread wait for network I/O.
    private Task QueueWatcherReset(string? directoryPath, bool dispose = false)
    {
        int version = ++_watcherVersion;
        _watcherTask = _watcherTask.ContinueWith(
            _ => ResetWatcher(directoryPath, version, dispose),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
        return _watcherTask;
    }

    private void ResetWatcher(string? directoryPath, int version, bool dispose)
    {
        if (version != Volatile.Read(ref _watcherVersion))
        {
            return;
        }

        try
        {
            _watcher.Stop();
            if (dispose)
            {
                _watcher.Dispose();
                return;
            }

            if (directoryPath is null || version != Volatile.Read(ref _watcherVersion))
            {
                return;
            }

            _watcher.Start(directoryPath);
        }
        catch (Exception exception) when (IsRecoverableError(exception))
        {
        }
        catch (Exception exception)
        {
            WatcherFailed?.Invoke(exception);
        }
    }

    private async Task ScanAfterWatcherAsync(string directoryPath, Task watcherReady, CancellationToken token)
    {
        await watcherReady.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (!token.IsCancellationRequested)
        {
            await ScanAsync(directoryPath, token).ConfigureAwait(false);
        }
    }

    private void ScheduleDebounceIfSupported(string path)
    {
        if (_scanner.IsProbablySupported(path))
        {
            ScheduleDebounce();
        }
    }

    private void ScheduleDebounceIfSupported(string path, string oldPath)
    {
        if (_scanner.IsProbablySupported(path) || _scanner.IsProbablySupported(oldPath))
        {
            ScheduleDebounce();
        }
    }

    private void ScheduleDebounce()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                if (_debounceMilliseconds <= 0)
                {
                    HandleDebounceTimer();
                }
                else
                {
                    _debounceTimer.Change(_debounceMilliseconds, Timeout.Infinite);
                }
            }
        }
    }

    private void HandleDebounceTimer()
    {
        string? directory;
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_disposed || CurrentDirectory is null)
            {
                return;
            }

            directory = CurrentDirectory;
            _scanCts?.Cancel();
            _scanCts?.Dispose();
            cts = new CancellationTokenSource();
            _scanCts = cts;
        }

        _ = ScanAsync(directory, cts.Token);
    }

    private async Task ScanAsync(string directoryPath, CancellationToken token)
    {
        FolderEntry[] files = [];
        string? error = null;
        try
        {
            files = await _scanner.ScanAsync(directoryPath, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (IsRecoverableError(exception))
        {
            error = exception.Message;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        PostToOwner(() =>
        {
            if (_disposed || token.IsCancellationRequested)
            {
                return;
            }

            Updated?.Invoke(new FolderUpdate(files, error));
        });
    }

    private void PostToOwner(Action action) => _ownerContext.Post(_ => action(), null);

    private static bool IsRecoverableError(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or OverflowException;
    }
}
