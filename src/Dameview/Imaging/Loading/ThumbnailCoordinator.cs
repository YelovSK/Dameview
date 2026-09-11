using Dameview.Platform;

namespace Dameview.Imaging;

internal enum ThumbnailPriority
{
    Gallery,
    Foreground,
}

internal interface IThumbnailLoader
{
    /// <summary>Queues thread-safe thumbnail work whose completion is dispatched to the UI thread.</summary>
    public IDisposable Request(
        string path,
        ThumbnailPriority priority,
        Action<DecodedImage> completed);
}

internal sealed class ThumbnailCoordinator : IThumbnailLoader, IDisposable
{
    private const long DefaultCacheCapacityBytes = 64L * 1024L * 1024L;

    private readonly object _sync = new();
    private readonly SynchronizationContext _uiContext;
    private readonly Func<string, DecodedImage?> _load;
    private readonly DecodedImageCache _cache;
    private readonly Dictionary<string, List<ThumbnailSubscription>> _pending =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly BackgroundQueue<object?> _queue;
    private bool _stopping;

    internal ThumbnailCoordinator(
        Func<string, DecodedImage?> load,
        SynchronizationContext uiContext)
    {
        _load = load;
        _uiContext = uiContext;
        _cache = new DecodedImageCache(DefaultCacheCapacityBytes);
        _queue = new BackgroundQueue<object?>("Dameview thumbnails", 1, static () => null);
    }

    public IDisposable Request(
        string path,
        ThumbnailPriority priority,
        Action<DecodedImage> completed)
    {
        var subscription = new ThumbnailSubscription(completed);
        DecodedImage? cached = null;
        bool enqueue = false;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (_cache.TryGet(path, out cached))
            {
                // Delivery stays asynchronous, through the same UI boundary as a load.
            }
            else if (_pending.TryGetValue(path, out List<ThumbnailSubscription>? pending))
            {
                pending.Add(subscription);
            }
            else
            {
                _pending.Add(path, [subscription]);
                enqueue = true;
            }
        }

        if (cached is not null)
        {
            DecodedImage image = cached;
            Post(() => subscription.Complete(image));
        }
        else if (enqueue)
        {
            _ = LoadAsync(path, priority);
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

    private async Task LoadAsync(string path, ThumbnailPriority priority)
    {
        DecodedImage? image = null;
        try
        {
            image = await _queue.Enqueue(
                (_, _) => Load(path),
                (int)priority).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Missing or broken shell thumbnails are represented by null.
        }

        if (image is null)
        {
            return;
        }

        List<ThumbnailSubscription> subscriptions;
        lock (_sync)
        {
            subscriptions = [];
            if (_pending.Remove(path, out List<ThumbnailSubscription>? pending))
            {
                subscriptions = pending;
            }
        }

        DecodedImage loaded = image;
        foreach (ThumbnailSubscription subscription in subscriptions)
        {
            Post(() => subscription.Complete(loaded));
        }
    }

    private DecodedImage? Load(string path)
    {
        lock (_sync)
        {
            if (_stopping)
            {
                return null;
            }

            if (_pending.TryGetValue(path, out List<ThumbnailSubscription>? pending)
                && pending.All(subscription => subscription.IsCancelled))
            {
                _pending.Remove(path);
                return null;
            }
        }

        try
        {
            DecodedImage? image = _load(path);
            if (image is not null)
            {
                _cache.Add(path, image);
            }

            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Post(Action action)
    {
        _uiContext.Post(
            _ =>
            {
                lock (_sync)
                {
                    if (_stopping)
                    {
                        return;
                    }
                }

                action();
            },
            null);
    }

    private sealed class ThumbnailSubscription(Action<DecodedImage> completed) : IDisposable
    {
        private int _cancelled;

        internal bool IsCancelled => Volatile.Read(ref _cancelled) != 0;

        internal void Complete(DecodedImage image)
        {
            if (!IsCancelled)
            {
                completed(image);
            }
        }

        public void Dispose() => Interlocked.Exchange(ref _cancelled, 1);
    }
}
