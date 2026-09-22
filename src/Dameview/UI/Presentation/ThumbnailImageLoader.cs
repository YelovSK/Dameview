using System.Diagnostics.CodeAnalysis;
using Dameview.Imaging;
using Dameview.Imaging.Loading;
using Dameview.Rendering;
using Vortice.Direct2D1;

namespace Dameview.UI.Presentation;

/// <summary>
/// UI-thread facade over the background thumbnail source. Uploaded thumbnails are
/// owned by a <see cref="RenderBitmapCache"/> and handed out as leases; callers
/// release their lease when the thumbnail is no longer displayed.
/// </summary>
internal interface IThumbnailImageLoader
{
    /// <summary>
    /// Requests a cached thumbnail bitmap, delivering the lease on the UI thread.
    /// The returned subscription cancels delivery; the lease is disposed if the
    /// request is cancelled before it is delivered.
    /// </summary>
    public IDisposable Request(
        string path,
        ThumbnailPriority priority,
        Action<CachedBitmapLease> completed);
}

internal sealed class ThumbnailImageLoader : IThumbnailImageLoader
{
    private readonly IThumbnailLoader _source;
    private readonly RenderBitmapCache _cache;
    private readonly SynchronizationContext _uiContext;
    private readonly Func<DecodedImageUpload, ID2D1Bitmap1> _createBitmap;

    internal ThumbnailImageLoader(
        IThumbnailLoader source,
        RenderBitmapCache cache,
        Func<ID2D1DeviceContext> deviceContext,
        SynchronizationContext uiContext)
        : this(
            source,
            cache,
            uiContext,
            // Resolved per upload, since the device can be replaced.
            image => D2DBitmapFactory.Create(deviceContext(), image))
    {
    }

    internal ThumbnailImageLoader(
        IThumbnailLoader source,
        RenderBitmapCache cache,
        SynchronizationContext uiContext,
        Func<DecodedImageUpload, ID2D1Bitmap1> createBitmap)
    {
        _source = source;
        _cache = cache;
        _uiContext = uiContext;
        _createBitmap = createBitmap;
    }

    public IDisposable Request(
        string path,
        ThumbnailPriority priority,
        Action<CachedBitmapLease> completed)
    {
        if (_cache.TryAcquire(path, out CachedBitmapLease? cached))
        {
            _cache.Trim();
            var subscription = new CachedLeaseSubscription(cached);
            _uiContext.Post(
                _ =>
                {
                    if (subscription.TryTake(out CachedBitmapLease? lease))
                    {
                        completed(lease);
                    }
                },
                null);
            return subscription;
        }

        return _source.Request(path, priority, image => Complete(path, image, completed));
    }

    private void Complete(
        string path,
        DecodedImageUpload image,
        Action<CachedBitmapLease> completed)
    {
        if (_cache.TryAcquire(path, out CachedBitmapLease? existing))
        {
            completed(existing);
            _cache.Trim();
            return;
        }

        ID2D1Bitmap1? bitmap = null;
        CachedBitmapLease lease;
        try
        {
            bitmap = _createBitmap(image);
            lease = _cache.AddAndAcquire(path, bitmap, image.Width, image.Height);
            bitmap = null;
        }
        catch
        {
            if (bitmap is not null)
            {
                _cache.DisposeUncached(bitmap);
            }

            // Thumbnails are best-effort; the consumer keeps its placeholder.
            return;
        }

        completed(lease);
        _cache.Trim();
    }

    // Owns a lease between acquisition and posted delivery. Disposing before the
    // post runs cancels the delivery and releases the lease.
    private sealed class CachedLeaseSubscription(CachedBitmapLease lease) : IDisposable
    {
        private CachedBitmapLease? _lease = lease;

        internal bool TryTake([NotNullWhen(true)] out CachedBitmapLease? value)
        {
            value = Interlocked.Exchange(ref _lease, null);
            return value is not null;
        }

        public void Dispose() => Interlocked.Exchange(ref _lease, null)?.Dispose();
    }
}
