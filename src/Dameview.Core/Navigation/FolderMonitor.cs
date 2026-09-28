namespace Dameview.Navigation;

/// <summary>
/// Appended entries add to those of earlier updates. Otherwise the entries are the whole folder.
/// </summary>
internal sealed record FolderUpdate(
    FolderEntry[] Entries,
    bool Appended,
    string? Error);

internal interface IFolderMonitor : IDisposable
{
    public event Action<FolderUpdate>? Updated;

    public FolderScope? Scope { get; }

    public void Open(FolderScope scope);
    public void Close();
}

internal sealed class FolderMonitor : IFolderMonitor
{
    private const int DefaultDebounceMilliseconds = 150;
    private static readonly TimeSpan DefaultProgressInterval = TimeSpan.FromMilliseconds(100);

    private readonly IFolderScanner _scanner;
    private readonly IFolderWatcher _watcher;
    private readonly SynchronizationContext _ownerContext;
    private readonly int _debounceMilliseconds;
    private readonly TimeSpan _progressInterval;
    private readonly Lock _gate = new();
    private readonly Timer _debounceTimer;
    private Task _watcherTask = Task.CompletedTask;
    private CancellationTokenSource? _scanCts;
    private int _watcherVersion;
    private bool _scanning;
    private bool _rescanPending;
    private bool _disposed;

    internal FolderMonitor(
        IFolderScanner scanner,
        IFolderWatcher watcher,
        SynchronizationContext ownerContext,
        int debounceMilliseconds = DefaultDebounceMilliseconds,
        TimeSpan? progressInterval = null)
    {
        _scanner = scanner;
        _watcher = watcher;
        _ownerContext = ownerContext;
        _debounceMilliseconds = debounceMilliseconds;
        _progressInterval = progressInterval ?? DefaultProgressInterval;
        _debounceTimer = new Timer(_ => HandleDebounceTimer(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher.Changed += ScheduleDebounceIfIncluded;
        _watcher.Created += ScheduleDebounceIfIncluded;
        _watcher.Deleted += ScheduleDebounceIfIncluded;
        _watcher.Renamed += ScheduleDebounceIfIncluded;
        _watcher.Error += ScheduleDebounce;
    }

    public event Action<FolderUpdate>? Updated;
    internal event Action<Exception>? WatcherFailed;

    public FolderScope? Scope { get; private set; }

    public void Open(FolderScope scope)
    {
        CancellationToken token;
        Task watcherReady;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Scope = scope;
            _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            token = BeginScan();
            watcherReady = QueueWatcherReset(scope);
        }

        _ = ScanAfterWatcherAsync(scope, watcherReady, token);
    }

    public void Close()
    {
        lock (_gate)
        {
            Scope = null;
            CancelScan();
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
            Scope = null;
            CancelScan();
            _debounceTimer.Dispose();
            QueueWatcherReset(null, dispose: true);
        }
    }

    // Watcher operations stay ordered without making the owner thread wait for network I/O.
    private Task QueueWatcherReset(FolderScope? scope, bool dispose = false)
    {
        int version = ++_watcherVersion;
        _watcherTask = _watcherTask.ContinueWith(
            _ => ResetWatcher(scope, version, dispose),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
        return _watcherTask;
    }

    private void ResetWatcher(FolderScope? scope, int version, bool dispose)
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

            if (scope is null || version != Volatile.Read(ref _watcherVersion))
            {
                return;
            }

            _watcher.Start(scope);
        }
        catch (Exception exception) when (IsRecoverableError(exception))
        {
        }
        catch (Exception exception)
        {
            WatcherFailed?.Invoke(exception);
        }
    }

    private async Task ScanAfterWatcherAsync(FolderScope scope, Task watcherReady, CancellationToken token)
    {
        await watcherReady.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (!token.IsCancellationRequested)
        {
            await ScanAsync(scope, progressive: true, token).ConfigureAwait(false);
        }
    }

    private void ScheduleDebounceIfIncluded(string path)
    {
        if (WouldInclude(path))
        {
            ScheduleDebounce();
        }
    }

    private void ScheduleDebounceIfIncluded(string path, string oldPath)
    {
        if (WouldInclude(path) || WouldInclude(oldPath))
        {
            ScheduleDebounce();
        }
    }

    private bool WouldInclude(string path) =>
        Scope is { } scope && _scanner.WouldInclude(scope, path);

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
        FolderScope scope;
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed || Scope is null)
            {
                return;
            }

            // Restarting the scan on every change would never let one finish in a busy tree.
            if (_scanning)
            {
                _rescanPending = true;
                return;
            }

            scope = Scope;
            token = BeginScan();
        }

        _ = ScanAsync(scope, progressive: false, token);
    }

    private CancellationToken BeginScan()
    {
        CancelScan();
        _scanCts = new CancellationTokenSource();
        _scanning = true;
        return _scanCts.Token;
    }

    private void CancelScan()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;
        _scanning = false;
        _rescanPending = false;
    }

    // The first scan of a scope reports what it has found so far, so a large tree fills in
    // gradually. Rescans replace the entries in one go, so the list never shrinks midway.
    private async Task ScanAsync(FolderScope scope, bool progressive, CancellationToken token)
    {
        var found = new List<FolderEntry>();
        IEnumerable<FolderEntry> entries = _scanner.Scan(scope, token);
        Task<string?> enumeration = Task.Run(() => Enumerate(entries, found));
        while (progressive)
        {
            var tick = Task.Delay(_progressInterval, token);
            if (await Task.WhenAny(enumeration, tick).ConfigureAwait(false) == enumeration
                || token.IsCancellationRequested)
            {
                break;
            }

            FolderEntry[] batch = Take(found);
            if (batch.Length > 0)
            {
                Deliver(new FolderUpdate(batch, Appended: true, Error: null), token);
            }
        }

        string? error;
        try
        {
            error = await enumeration.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }

        bool rescan;
        lock (_gate)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            rescan = _rescanPending;
            _scanning = false;
            _rescanPending = false;
        }

        Deliver(error is null
            ? new FolderUpdate(Take(found), Appended: progressive, Error: null)
            : new FolderUpdate([], Appended: false, error), token);
        if (rescan)
        {
            ScheduleDebounce();
        }
    }

    private static string? Enumerate(IEnumerable<FolderEntry> entries, List<FolderEntry> found)
    {
        try
        {
            foreach (FolderEntry entry in entries)
            {
                lock (found)
                {
                    found.Add(entry);
                }
            }

            return null;
        }
        catch (Exception exception) when (IsRecoverableError(exception))
        {
            return exception.Message;
        }
    }

    private static FolderEntry[] Take(List<FolderEntry> found)
    {
        lock (found)
        {
            FolderEntry[] taken = [.. found];
            found.Clear();
            return taken;
        }
    }

    private void Deliver(FolderUpdate update, CancellationToken token)
    {
        PostToOwner(() =>
        {
            if (!_disposed && !token.IsCancellationRequested)
            {
                Updated?.Invoke(update);
            }
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
