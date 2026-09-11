using Dameview.Imaging;

namespace Dameview.UI.Panels;

internal sealed class TileDecodeScheduler : IDisposable
{
    internal const int MaximumPendingTiles = 64;
    internal static readonly int DefaultMaximumWorkers = Math.Clamp(
        Environment.ProcessorCount / 2,
        2,
        8);

    private readonly IImageTileSource _source;
    private readonly SynchronizationContext _uiContext;
    private readonly Action<ImageTile, DecodedImage?> _completed;
    private readonly int _maximumWorkers;
    private readonly Lock _gate = new();
    private readonly List<ImageTile> _pending = [];
    private readonly HashSet<ImageTile> _scheduled = [];
    private readonly CancellationTokenSource _cancellation = new();
    private int _activeWorkers;
    private bool _disposed;

    internal TileDecodeScheduler(
        IImageTileSource source,
        Action<ImageTile, DecodedImage?> completed,
        int? maximumWorkers = null,
        SynchronizationContext? uiContext = null)
    {
        _source = source;
        _completed = completed;
        _maximumWorkers = maximumWorkers ?? DefaultMaximumWorkers;
        _uiContext = uiContext
            ?? SynchronizationContext.Current
            ?? throw new InvalidOperationException("Tile decoding requires a UI SynchronizationContext.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_maximumWorkers);
    }

    internal void ReplaceRequests(IReadOnlyList<ImageTile> prioritizedTiles)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            foreach (ImageTile tile in _pending)
            {
                _scheduled.Remove(tile);
            }

            _pending.Clear();
            int count = Math.Min(prioritizedTiles.Count, MaximumPendingTiles);
            for (int index = count - 1; index >= 0; index--)
            {
                ImageTile tile = prioritizedTiles[index];
                if (_scheduled.Add(tile))
                {
                    _pending.Add(tile);
                }
            }

        }

        StartAvailableWorkers();
    }

    private void ProcessRequests()
    {
        CancellationToken token = _cancellation.Token;
        if (!TryTakeNext(out ImageTile tile))
        {
            FinishWorker();
            return;
        }

        try
        {
            using IImageTileDecoder decoder = _source.CreateTileDecoder();
            while (!token.IsCancellationRequested)
            {
                try
                {
                    DecodedImage image = decoder.DecodeTile(tile, token);
                    Publish(tile, image, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    Publish(tile, null, token);
                }

                if (!TryTakeNext(out tile))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch
        {
            Publish(tile, null, token);
        }
        finally
        {
            FinishWorker();
        }
    }

    private void StartAvailableWorkers()
    {
        int workersToStart;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            workersToStart = Math.Min(
                _pending.Count,
                _maximumWorkers - _activeWorkers);
            _activeWorkers += workersToStart;
        }

        for (int index = 0; index < workersToStart; index++)
        {
            _ = Task.Run(ProcessRequests);
        }
    }

    private bool TryTakeNext(out ImageTile tile)
    {
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                tile = default;
                return false;
            }

            int index = _pending.Count - 1;
            tile = _pending[index];
            _pending.RemoveAt(index);
            return true;
        }
    }

    private void FinishWorker()
    {
        lock (_gate)
        {
            _activeWorkers--;
        }

        StartAvailableWorkers();
    }

    private void Publish(
        ImageTile tile,
        DecodedImage? image,
        CancellationToken token)
    {
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _uiContext.Post(_ =>
            {
                try
                {
                    _completed(tile, image);
                }
                finally
                {
                    lock (_gate)
                    {
                        _scheduled.Remove(tile);
                    }

                    published.TrySetResult();
                }
            }, null);
            // Do not let decoded CPU buffers accumulate behind the UI thread.
            published.Task.Wait(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch
        {
            lock (_gate)
            {
                _scheduled.Remove(tile);
            }
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
            _pending.Clear();
            _scheduled.Clear();
        }

        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
