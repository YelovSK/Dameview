namespace Dameview.Navigation;

/// <summary>
/// Appended entries add to those of earlier updates. Otherwise the entries are the whole folder.
/// A scan sends updates until one that has finished it.
/// </summary>
internal sealed record FolderUpdate(
    FolderEntry[] Entries,
    bool Appended,
    bool ScanFinished,
    string? Error);

/// <summary>
/// Scans a scope and rescans it when the watcher sees it change. Keeps what it found, so that
/// everyone showing the scope can share it. The entries are owner-thread confined.
/// </summary>
internal sealed class FolderSource : IDisposable
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
    private readonly List<FolderEntry> _entries = [];
    private Task _watcherTask = Task.CompletedTask;
    private CancellationTokenSource? _scanCts;
    private bool _scanning;
    private bool _rescanPending;
    private bool _scanFinished;
    private string? _error;
    private bool _updated;
    private bool _disposed;

    internal FolderSource(
        FolderScope scope,
        IFolderScanner scanner,
        IFolderWatcher watcher,
        SynchronizationContext ownerContext,
        int debounceMilliseconds = DefaultDebounceMilliseconds,
        TimeSpan? progressInterval = null)
    {
        Scope = scope;
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

    /// <summary>Raised after the update is applied to <see cref="Snapshot"/>.</summary>
    internal event Action<FolderUpdate>? Updated;
    internal event Action<Exception>? WatcherFailed;

    internal FolderScope Scope { get; }

    /// <summary>Everything found so far as one update, or null before the first update.</summary>
    internal FolderUpdate? Snapshot => _updated
        ? new FolderUpdate([.. _entries], Appended: false, _scanFinished, _error)
        : null;

    // The scan waits for the watcher, so that no change between the two goes unseen.
    internal void Start()
    {
        CancellationToken token;
        Task watcherStarted;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token = BeginScan();
            watcherStarted = _watcherTask = RunWatcherAsync(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    _watcher.Start(Scope);
                }
            });
        }

        _ = ScanAfterWatcherAsync(watcherStarted, token);
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
            CancelScan();
            _debounceTimer.Dispose();
            _watcherTask = RunWatcherAsync(_watcher.Dispose);
        }
    }

    // Watcher operations stay ordered without making the owner thread wait for network I/O.
    private Task RunWatcherAsync(Action operation)
    {
        return _watcherTask.ContinueWith(
            _ =>
            {
                try
                {
                    operation();
                }
                catch (Exception exception) when (IsRecoverableError(exception))
                {
                }
                catch (Exception exception)
                {
                    WatcherFailed?.Invoke(exception);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private async Task ScanAfterWatcherAsync(Task watcherStarted, CancellationToken token)
    {
        await watcherStarted.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (!token.IsCancellationRequested)
        {
            await ScanAsync(progressive: true, token).ConfigureAwait(false);
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

    private bool WouldInclude(string path) => _scanner.WouldInclude(Scope, path);

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
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // Restarting the scan on every change would never let one finish in a busy tree.
            if (_scanning)
            {
                _rescanPending = true;
                return;
            }

            token = BeginScan();
        }

        _ = ScanAsync(progressive: false, token);
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
    private async Task ScanAsync(bool progressive, CancellationToken token)
    {
        var found = new List<FolderEntry>();
        IEnumerable<FolderEntry> entries = _scanner.Scan(Scope, token);
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
                Deliver(new FolderUpdate(batch, Appended: true, ScanFinished: false, Error: null), token);
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
            ? new FolderUpdate(Take(found), Appended: progressive, ScanFinished: true, Error: null)
            : new FolderUpdate([], Appended: false, ScanFinished: true, error), token);
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
                Apply(update);
                Updated?.Invoke(update);
            }
        });
    }

    private void Apply(FolderUpdate update)
    {
        if (!update.Appended)
        {
            _entries.Clear();
        }

        _entries.AddRange(update.Entries);
        _error = update.Error;
        _scanFinished |= update.ScanFinished;
        _updated = true;
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
