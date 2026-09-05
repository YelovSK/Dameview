using System.Diagnostics.CodeAnalysis;
using Dameview.Platform;

namespace Dameview.Imaging;

internal enum ThumbnailPriority
{
    Gallery,
    Foreground,
}

internal interface IThumbnailLoader
{
    public IDisposable Request(
        string path,
        ThumbnailPriority priority,
        Action<DecodedImage> completed);
}

internal sealed class ThumbnailCoordinator : IThumbnailLoader, IDisposable
{
    private const long DefaultCacheCapacityBytes = 64L * 1024L * 1024L;

    private readonly object _sync = new();
    private readonly Action<Action> _postToUi;
    private readonly Func<string, DecodedImage?> _load;
    private readonly DecodedImageCache _cache;
    private readonly Queue<string> _foregroundQueue = new();
    private readonly Queue<string> _galleryQueue = new();
    private readonly Dictionary<string, PendingThumbnail> _pending =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Thread _worker;
    private bool _stopping;

    internal ThumbnailCoordinator(
        Action<Action> postToUi,
        Func<string, DecodedImage?> load,
        long cacheCapacityBytes = DefaultCacheCapacityBytes)
    {
        _postToUi = postToUi;
        _load = load;
        _cache = new DecodedImageCache(cacheCapacityBytes);
        _worker = new Thread(Work)
        {
            IsBackground = true,
            Name = "Dameview thumbnails",
        };
        _worker.Start();
    }

    public IDisposable Request(
        string path,
        ThumbnailPriority priority,
        Action<DecodedImage> completed)
    {
        var subscription = new ThumbnailSubscription(completed);
        DecodedImage? cached = null;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (_cache.TryGet(path, out cached))
            {
                // Delivery remains asynchronous and uses the same UI boundary as a load.
            }
            else if (_pending.TryGetValue(path, out PendingThumbnail? pending))
            {
                pending.Subscriptions.Add(subscription);
                if (!pending.IsLoading && priority > pending.Priority)
                {
                    pending.Priority = priority;
                    _foregroundQueue.Enqueue(path);
                    Monitor.Pulse(_sync);
                }
            }
            else
            {
                pending = new PendingThumbnail(priority, subscription);
                _pending.Add(path, pending);
                GetQueue(priority).Enqueue(path);
                Monitor.Pulse(_sync);
            }
        }

        if (cached is not null)
        {
            Post(subscription, cached);
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
            _foregroundQueue.Clear();
            _galleryQueue.Clear();
            _pending.Clear();
            Monitor.PulseAll(_sync);
        }
    }

    private void Work()
    {
        NativeMethods.InitializeComApartment(ComApartment.MultiThreaded);
        try
        {
            while (TryTakeWork(out string? path, out PendingThumbnail? pending))
            {
                DecodedImage? image = null;
                try
                {
                    image = _load(path);
                    if (image is not null)
                    {
                        _cache.Add(path, image);
                    }
                }
                catch (Exception)
                {
                    // Missing or broken shell thumbnails are represented by null.
                }

                List<ThumbnailSubscription> subscriptions;
                lock (_sync)
                {
                    if (!_pending.Remove(path, out PendingThumbnail? current)
                        || !ReferenceEquals(current, pending))
                    {
                        continue;
                    }

                    subscriptions = current.Subscriptions;
                }

                if (image is null)
                {
                    continue;
                }

                foreach (ThumbnailSubscription subscription in subscriptions)
                {
                    Post(subscription, image);
                }
            }
        }
        finally
        {
            NativeMethods.UninitializeComApartment();
        }
    }

    private bool TryTakeWork(
        [NotNullWhen(true)] out string? path,
        [NotNullWhen(true)] out PendingThumbnail? pending)
    {
        lock (_sync)
        {
            while (!_stopping)
            {
                if (TryTakeQueue(_foregroundQueue, ThumbnailPriority.Foreground, out path, out pending)
                    || TryTakeQueue(_galleryQueue, ThumbnailPriority.Gallery, out path, out pending))
                {
                    return true;
                }

                Monitor.Wait(_sync);
            }

            path = null;
            pending = null;
            return false;
        }
    }

    private bool TryTakeQueue(
        Queue<string> queue,
        ThumbnailPriority priority,
        [NotNullWhen(true)] out string? path,
        [NotNullWhen(true)] out PendingThumbnail? pending)
    {
        while (queue.TryDequeue(out path))
        {
            if (!_pending.TryGetValue(path, out pending)
                || pending.IsLoading
                || pending.Priority != priority)
            {
                continue;
            }

            if (pending.Subscriptions.All(subscription => subscription.IsCancelled))
            {
                _pending.Remove(path);
                continue;
            }

            pending.IsLoading = true;
            return true;
        }

        path = null;
        pending = null;
        return false;
    }

    private Queue<string> GetQueue(ThumbnailPriority priority) =>
        priority == ThumbnailPriority.Foreground ? _foregroundQueue : _galleryQueue;

    private void Post(ThumbnailSubscription subscription, DecodedImage image)
    {
        _postToUi(() =>
        {
            lock (_sync)
            {
                if (_stopping || subscription.IsCancelled)
                {
                    return;
                }
            }

            subscription.Complete(image);
        });
    }

    private sealed class PendingThumbnail
    {
        internal PendingThumbnail(ThumbnailPriority priority, ThumbnailSubscription subscription)
        {
            Priority = priority;
            Subscriptions = [subscription];
        }

        internal ThumbnailPriority Priority { get; set; }
        internal bool IsLoading { get; set; }
        internal List<ThumbnailSubscription> Subscriptions { get; }
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
