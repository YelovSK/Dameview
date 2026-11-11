using Dameview.Diagnostics;
using Dameview.Rendering;
using Dameview.Win32;
using Vortice.Direct2D1;

namespace Dameview.Imaging.Loading;

// Each wanted image is one job, however many requests share it. A job is loaded by its
// variant's source on that source's workers, uploaded to the GPU on the same worker, and
// cached on the window thread, which is also where all of the bookkeeping here happens.
internal sealed class ImagePipeline : IImagePipeline, IDisposable
{
    private readonly SynchronizationContext _uiContext;
    private readonly IBitmapUploader _uploader;
    private readonly Store[] _stores;
    private readonly Dictionary<ImageKey, Job> _jobs = [];
    private bool _disposed;

    internal ImagePipeline(
        SynchronizationContext uiContext,
        IBitmapUploader uploader,
        (IImageSource Source, RenderBitmapCache Cache) full,
        (IImageSource Source, RenderBitmapCache Cache) thumbnails)
    {
        _uiContext = uiContext;
        _uploader = uploader;
        _stores = new Store[2];
        _stores[(int)ImageVariant.Full] = new Store(full.Source, full.Cache);
        _stores[(int)ImageVariant.Thumbnail] = new Store(thumbnails.Source, thumbnails.Cache);
    }

    /// <summary>For moving the cached bitmaps to a new device.</summary>
    internal IEnumerable<RenderBitmapCache> Caches => _stores.Select(store => store.Cache);

    /// <summary>Keeps preloads from starting until <see cref="ReleasePreloads"/>.</summary>
    internal void HoldPreloads()
    {
        foreach (Store store in _stores)
        {
            store.HoldPreloads();
        }
    }

    internal void ReleasePreloads()
    {
        foreach (Store store in _stores)
        {
            store.ReleasePreloads();
        }
    }

    public IDisposable Request(ImageKey key, ImagePriority priority, Action<ImageLoadResult> completed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Store store = _stores[(int)key.Variant];
        if (store.Cache.TryAcquire(key.Path, out CachedBitmapLease? lease))
        {
            completed(new ImageLoaded(key.Path, lease));
            return Delivered.Instance;
        }

        if (!_jobs.TryGetValue(key, out Job? job))
        {
            job = new Job(key, store);
            _jobs.Add(key, job);
        }

        var subscription = new Subscription(this, job, priority, completed);
        job.Subscriptions.Add(subscription);
        if (priority > job.Priority)
        {
            job.Priority = priority;
        }

        if (job.Attempt is null)
        {
            Start(job);
        }
        else if (job.Priority > job.Attempt.QueuedPriority && job.Attempt.TryWithdraw())
        {
            // Still queued, so it can be queued again further ahead.
            ImageRepresentation? loaded = job.Attempt.TakeLoaded();
            job.Attempt.Dispose();
            Start(job, loaded);
        }

        return subscription;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Job job in _jobs.Values)
        {
            job.Attempt?.Abandon();
        }

        _jobs.Clear();
        foreach (Store store in _stores)
        {
            store.Dispose();
        }
    }

    /// <param name="loaded">An image that only still needs uploading, which saves loading it again.</param>
    private void Start(Job job, ImageRepresentation? loaded = null)
    {
        var attempt = new Attempt(job, loaded);
        job.Attempt = attempt;
        // The work posts its own result. Only a queue that failed to start never runs it.
        job.Store.QueueFor(attempt.QueuedPriority)
            .Enqueue((state, _) => Run(job, attempt, state), (int)attempt.QueuedPriority, attempt.CancellationToken)
            .ContinueWith(
                task => Post(job, attempt, task.Exception!.GetBaseException()),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
    }

    // On a worker.
    private void Run(Job job, Attempt attempt, object? workerState)
    {
        if (!attempt.TryStart())
        {
            return;
        }

        ImageRepresentation? image = null;
        try
        {
            image = attempt.TakeLoaded() ?? job.Store.Source.Load(workerState, job.Key.Path, attempt);
            if (image is { IsStatic: true } && attempt.ShouldUpload(image))
            {
                attempt.Uploaded = _uploader.Upload(image);
            }
        }
        catch (Exception exception)
        {
            image?.Dispose();
            Post(job, attempt, exception);
            return;
        }

        Post(job, attempt, image is null ? default(LoadOutcome?) : image);
    }

    // A null outcome means the source stopped without loading anything.
    private void Post(Job job, Attempt attempt, LoadOutcome? outcome) =>
        _uiContext.Post(_ => Finish(job, attempt, outcome), null);

    private void Finish(Job job, Attempt attempt, LoadOutcome? outcome)
    {
        if (_disposed)
        {
            if (outcome is ImageRepresentation image)
            {
                image.Dispose();
            }

            attempt.Dispose();
            return;
        }

        switch (outcome)
        {
            case Exception failure when !attempt.IsCancelled:
                Fail(job, attempt, failure);
                break;

            case ImageRepresentation { IsStatic: false } animated:
                attempt.Dispose();
                Hand(job, animated);
                break;

            case ImageRepresentation image when attempt.Uploaded is not null:
                Cache(job, attempt, image);
                break;

            case ImageRepresentation skipped:
                skipped.Dispose();
                Stop(job, attempt);
                break;

            default:
                Stop(job, attempt);
                break;
        }
    }

    // Stopped because nobody wanted it anymore, or skipped as a preload that would not be kept.
    // Whoever asked for it since then still gets it.
    private void Stop(Job job, Attempt attempt)
    {
        attempt.Dispose();
        if (job.Subscriptions.Count > 0 && (attempt.IsCancelled || job.Priority > ImagePriority.Preload))
        {
            Start(job);
        }
        else
        {
            Remove(job);
        }
    }

    private void Cache(Job job, Attempt attempt, ImageRepresentation image)
    {
        if (!attempt.Uploaded!.TryCreateBitmap(out ID2D1Bitmap1? bitmap))
        {
            // The device was switched right after the upload, so only the upload is done again.
            attempt.Dispose();
            Start(job, image);
            return;
        }

        // The pixels are on the GPU by now, so the image only tells the size.
        image.Dispose();
        Remove(job);
        // A delivery can dispose other subscriptions, which takes them out of the job.
        Subscription[] subscriptions = [.. job.Subscriptions];
        if (subscriptions.Length == 0)
        {
            bitmap.Dispose();
            attempt.Dispose();
            return;
        }

        RenderBitmapCache cache = job.Store.Cache;
        string path = job.Key.Path;
        // Leased up front, since a lease released by an earlier delivery can let the cache
        // evict the image before a later one.
        var leases = new CachedBitmapLease[subscriptions.Length];
        leases[0] = cache.Add(path, image.Width, image.Height, image.Orientation, bitmap);
        for (int index = 1; index < leases.Length; index++)
        {
            cache.TryAcquire(path, out CachedBitmapLease? lease);
            leases[index] = lease!;
        }

        // Releases the space a preload reserved, now that the image itself counts.
        attempt.Dispose();
        for (int index = 0; index < subscriptions.Length; index++)
        {
            subscriptions[index].Deliver(new ImageLoaded(path, leases[index]));
        }

        cache.Trim();
    }

    // A tiled or animated image has a single owner, so the others waiting on it load their own.
    private void Hand(Job job, ImageRepresentation image)
    {
        Remove(job);
        Subscription? owner = null;
        foreach (Subscription subscription in job.Subscriptions)
        {
            if (owner is null || subscription.Priority > owner.Priority)
            {
                owner = subscription;
            }
        }

        if (owner is null)
        {
            image.Dispose();
            return;
        }

        job.Subscriptions.Remove(owner);
        job.Priority = HighestPriority(job.Subscriptions);
        if (job.Priority > ImagePriority.Preload)
        {
            _jobs.Add(job.Key, job);
            Start(job);
        }

        owner.Deliver(new ImageLoaded(job.Key.Path, image));
    }

    private void Fail(Job job, Attempt attempt, Exception exception)
    {
        Remove(job);
        attempt.Dispose();
        Log.Debug("Image", $"Could not load {job.Key.Variant} of '{Path.GetFileName(job.Key.Path)}'.", exception);
        foreach (Subscription subscription in job.Subscriptions.ToArray())
        {
            subscription.Deliver(new ImageLoadFailed(job.Key.Path, exception));
        }
    }

    private void Remove(Job job)
    {
        _jobs.Remove(job.Key);
        job.Attempt = null;
    }

    private void Unsubscribe(Subscription subscription)
    {
        Job job = subscription.Job;
        if (!job.Subscriptions.Remove(subscription))
        {
            return;
        }

        job.Priority = HighestPriority(job.Subscriptions);
        if (job.Subscriptions.Count > 0 || job.Attempt is not { } attempt)
        {
            return;
        }

        // Nobody wants it anymore.
        if (attempt.Abandon())
        {
            Remove(job);
        }
    }

    private static ImagePriority HighestPriority(List<Subscription> subscriptions)
    {
        ImagePriority highest = ImagePriority.Preload;
        foreach (Subscription subscription in subscriptions)
        {
            if (subscription.Priority > highest)
            {
                highest = subscription.Priority;
            }
        }

        return highest;
    }

    private union LoadOutcome(ImageRepresentation, Exception);

    private sealed class Store(IImageSource source, RenderBitmapCache cache) : IDisposable
    {
        private readonly ComWorkerQueue<object?> _queue =
            new(source.WorkerName, source.WorkerCount, source.CreateWorkerState);
        // Workers of their own, so preloads never hold up an image on display. Started on the
        // first preload, which some sources never get.
        private ComWorkerQueue<object?>? _preloadQueue;
        private bool _preloadsHeld;

        internal IImageSource Source { get; } = source;
        internal RenderBitmapCache Cache { get; } = cache;

        internal ComWorkerQueue<object?> QueueFor(ImagePriority priority)
        {
            if (priority != ImagePriority.Preload)
            {
                return _queue;
            }

            if (_preloadQueue is null)
            {
                _preloadQueue = new($"{Source.WorkerName} preloads", 2, Source.CreateWorkerState);
                if (_preloadsHeld)
                {
                    _preloadQueue.Pause();
                }
            }

            return _preloadQueue;
        }

        internal void HoldPreloads()
        {
            _preloadsHeld = true;
            _preloadQueue?.Pause();
        }

        internal void ReleasePreloads()
        {
            _preloadsHeld = false;
            _preloadQueue?.Resume();
        }

        public void Dispose()
        {
            _queue.Dispose();
            _preloadQueue?.Dispose();
            Cache.Dispose();
        }
    }

    private sealed class Job(ImageKey key, Store store)
    {
        internal ImageKey Key { get; } = key;
        internal Store Store { get; } = store;
        internal List<Subscription> Subscriptions { get; } = [];
        internal Attempt? Attempt { get; set; }

        // Workers read it to tell whether the job is still only a preload.
        internal volatile ImagePriority Priority;
    }

    // One go at loading a job. It is replaced when the job is queued again at a higher priority.
    private sealed class Attempt(Job job, ImageRepresentation? loaded) : ImageLoadContext, IDisposable
    {
        private const int Queued = 0;
        private const int Running = 1;
        private const int Withdrawn = 2;

        private readonly CancellationTokenSource _cancellation = new();
        private int _state;
        private long _reservedBytes;
        private ImageRepresentation? _loaded = loaded;

        internal ImagePriority QueuedPriority { get; } = job.Priority;
        internal override CancellationToken CancellationToken => _cancellation.Token;
        internal override long CacheCapacityBytes => job.Store.Cache.CapacityBytes;
        internal bool IsCancelled => _cancellation.IsCancellationRequested;

        /// <summary>Set by the worker, once it has uploaded a static image.</summary>
        internal IUploadedBitmap? Uploaded { get; set; }

        // On a worker, which only runs the attempt if it hasn't been withdrawn.
        internal bool TryStart() => Interlocked.CompareExchange(ref _state, Running, Queued) == Queued;

        // Window thread. Takes the attempt back if no worker has started it yet.
        internal bool TryWithdraw()
        {
            if (Interlocked.CompareExchange(ref _state, Withdrawn, Queued) != Queued)
            {
                return false;
            }

            Cancel();
            return true;
        }

        internal void Cancel() => _cancellation.Cancel();

        /// <summary>Window thread. Drops a queued attempt and returns true, or asks a running one to stop.</summary>
        internal bool Abandon()
        {
            if (TryWithdraw())
            {
                Dispose();
                return true;
            }

            Cancel();
            return false;
        }

        internal ImageRepresentation? TakeLoaded() => Interlocked.Exchange(ref _loaded, null);

        internal override bool ShouldLoad(long? cachedBytes) =>
            job.Priority > ImagePriority.Preload || (cachedBytes is { } bytes && TryReserve(bytes));

        // On a worker. A preload is only uploaded if there is room to keep it, which sources
        // that can't tell the size up front haven't checked yet.
        internal bool ShouldUpload(ImageRepresentation image) =>
            !IsCancelled
            && (job.Priority > ImagePriority.Preload
                || TryReserve(DecodedImage.GetByteCount(image.Width, image.Height)));

        private bool TryReserve(long bytes)
        {
            if (_reservedBytes > 0)
            {
                return true;
            }

            if (!job.Store.Cache.TryReserve(bytes))
            {
                return false;
            }

            _reservedBytes = bytes;
            return true;
        }

        // Window thread, once whatever the attempt loaded is cached or gone.
        public void Dispose()
        {
            job.Store.Cache.Unreserve(_reservedBytes);
            _reservedBytes = 0;
            _loaded?.Dispose();
            Uploaded?.Dispose();
            _cancellation.Dispose();
        }
    }

    private sealed class Subscription(
        ImagePipeline owner,
        Job job,
        ImagePriority priority,
        Action<ImageLoadResult> completed) : IDisposable
    {
        private bool _done;

        internal Job Job { get; } = job;
        internal ImagePriority Priority { get; } = priority;

        internal void Deliver(ImageLoadResult result)
        {
            if (_done)
            {
                (result as ImageLoaded)?.Dispose();
                return;
            }

            _done = true;
            completed(result);
        }

        public void Dispose()
        {
            if (!_done)
            {
                _done = true;
                owner.Unsubscribe(this);
            }
        }
    }

    private sealed class Delivered : IDisposable
    {
        internal static readonly Delivered Instance = new();

        public void Dispose()
        {
        }
    }
}
