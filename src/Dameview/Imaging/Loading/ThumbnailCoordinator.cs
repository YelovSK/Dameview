using Dameview.Win32;

namespace Dameview.Imaging.Loading;

internal enum ThumbnailPriority
{
    Gallery,
    Foreground,
}

internal interface IThumbnailLoader
{
    /// <summary>
    /// Queues thread-safe thumbnail work whose completion is dispatched to the UI thread.
    /// The callback borrows the upload only for the duration of the callback; the loader
    /// retains ownership and disposes it after the callback returns.
    /// </summary>
    public IDisposable Request(
        string path,
        ThumbnailPriority priority,
        Action<DecodedImageUpload> completed);
}

internal sealed class ThumbnailCoordinator : IThumbnailLoader, IDisposable
{
    private readonly Lock _sync = new();
    private readonly SynchronizationContext _uiContext;
    private readonly Func<string, DecodedImageUpload?> _load;
    private readonly Dictionary<string, PendingThumbnail> _pending =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ComWorkerQueue<object?> _queue;
    private bool _stopping;

    internal ThumbnailCoordinator(
        Func<string, DecodedImageUpload?> load,
        SynchronizationContext uiContext)
    {
        _load = load;
        _uiContext = uiContext;
        _queue = new ComWorkerQueue<object?>("Dameview thumbnails", 4, static () => null);
    }

    public IDisposable Request(
        string path,
        ThumbnailPriority priority,
        Action<DecodedImageUpload> completed)
    {
        PendingThumbnail pending;
        ThumbnailSubscription subscription;
        PendingThumbnail? enqueue = null;
        CancellationToken enqueueCancellation = default;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (_pending.TryGetValue(path, out PendingThumbnail? existing))
            {
                subscription = new ThumbnailSubscription(
                    completed,
                    cancelled => CancelSubscription(path, existing, cancelled));
                existing.Subscriptions.Add(subscription);
                pending = existing;
                if (!existing.IsLoading && priority > existing.Priority)
                {
                    existing.Priority = priority;
                    existing.CancelQueuedLoad();
                    enqueue = existing;
                    enqueueCancellation = existing.CancellationToken;
                }
            }
            else
            {
                pending = new PendingThumbnail(priority);
                subscription = new ThumbnailSubscription(
                    completed,
                    cancelled => CancelSubscription(path, pending, cancelled));
                pending.Subscriptions.Add(subscription);
                _pending.Add(path, pending);
                enqueue = pending;
                enqueueCancellation = pending.CancellationToken;
            }
        }

        if (enqueue is not null)
        {
            _ = LoadAsync(path, enqueue, priority, enqueueCancellation);
        }

        return subscription;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            _pending.Clear();
        }

        _queue.Dispose();
    }

    private async Task LoadAsync(
        string path,
        PendingThumbnail request,
        ThumbnailPriority priority,
        CancellationToken cancellationToken)
    {
        DecodedImageUpload? image = null;
        try
        {
            image = await _queue.Enqueue(
                (_, _) => Load(path, request, priority),
                (int)priority,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Missing or broken shell thumbnails are represented by null.
        }

        List<ThumbnailSubscription> subscriptions;
        lock (_sync)
        {
            subscriptions = [];
            if (_pending.TryGetValue(path, out PendingThumbnail? pending)
                && ReferenceEquals(pending, request)
                && pending.Priority == priority
                && (pending.IsLoading || image is null))
            {
                _pending.Remove(path);
                subscriptions = pending.Subscriptions;
            }
        }

        if (image is null)
        {
            return;
        }

        try
        {
            foreach (ThumbnailSubscription subscription in subscriptions)
            {
                DecodedImageUpload delivery = image.Retain();
                Post(
                    () => subscription.Complete(delivery),
                    delivery);
            }
        }
        finally
        {
            image.Dispose();
        }
    }

    private void CancelSubscription(
        string path,
        PendingThumbnail request,
        ThumbnailSubscription subscription)
    {
        lock (_sync)
        {
            if (!_pending.TryGetValue(path, out PendingThumbnail? pending)
                || !ReferenceEquals(pending, request))
            {
                return;
            }

            pending.Subscriptions.Remove(subscription);
            if (!pending.IsLoading && pending.Subscriptions.Count == 0)
            {
                _pending.Remove(path);
                pending.CancelLoad();
            }
        }
    }

    private DecodedImageUpload? Load(
        string path,
        PendingThumbnail request,
        ThumbnailPriority priority)
    {
        lock (_sync)
        {
            if (_stopping)
            {
                return null;
            }

            if (!_pending.TryGetValue(path, out PendingThumbnail? pending)
                || !ReferenceEquals(pending, request)
                || pending.IsLoading
                || pending.Priority != priority)
            {
                return null;
            }

            if (pending.Subscriptions.All(subscription => subscription.IsCancelled))
            {
                _pending.Remove(path);
                return null;
            }

            pending.IsLoading = true;
        }

        try
        {
            return _load(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Post(Action action, DecodedImageUpload delivery)
    {
        try
        {
            _uiContext.Post(
                _ =>
                {
                    lock (_sync)
                    {
                        if (_stopping)
                        {
                            delivery.Dispose();
                            return;
                        }
                    }

                    try
                    {
                        action();
                    }
                    finally
                    {
                        delivery.Dispose();
                    }
                },
                null);
        }
        catch
        {
            delivery.Dispose();
            throw;
        }
    }

    private sealed class PendingThumbnail(ThumbnailPriority priority)
    {
        internal ThumbnailPriority Priority { get; set; } = priority;
        internal bool IsLoading { get; set; }
        internal CancellationTokenSource Cancellation { get; private set; } = new();
        internal CancellationToken CancellationToken => Cancellation.Token;
        internal List<ThumbnailSubscription> Subscriptions { get; } = [];

        internal void CancelQueuedLoad()
        {
            CancellationTokenSource previous = Cancellation;
            previous.Cancel();
            previous.Dispose();
            Cancellation = new CancellationTokenSource();
        }

        internal void CancelLoad()
        {
            CancellationTokenSource previous = Cancellation;
            previous.Cancel();
            previous.Dispose();
        }
    }

    private sealed class ThumbnailSubscription(
        Action<DecodedImageUpload> completed,
        Action<ThumbnailSubscription> cancelled) : IDisposable
    {
        private int _cancelled;

        internal bool IsCancelled => Volatile.Read(ref _cancelled) != 0;

        internal void Complete(DecodedImageUpload image)
        {
            if (!IsCancelled)
            {
                completed(image);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _cancelled, 1) == 0)
            {
                cancelled(this);
            }
        }
    }
}
