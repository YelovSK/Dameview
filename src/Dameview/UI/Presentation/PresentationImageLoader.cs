using Dameview.Diagnostics;
using Dameview.Imaging;
using Dameview.Imaging.Loading;
using Dameview.Rendering;
using Vortice.Direct2D1;

namespace Dameview.UI.Presentation;

// UI-thread facade: checks render resources before asking the background pixel
// producer, converts temporary uploads into cache-owned Direct2D bitmaps, and
// presents GPU thumbnails while the full image loads.
internal sealed class PresentationImageLoader : IImageLoader
{
    private readonly ImageLoadClient _producer;
    private readonly RenderBitmapCache _cache;
    private readonly IThumbnailImageLoader _thumbnails;
    private readonly SynchronizationContext _uiContext;
    private readonly Func<DecodedImageUpload, ID2D1Bitmap1> _createUploadBitmap;
    private readonly Func<DecodedImage, ID2D1Bitmap1> _createDecodedBitmap;
    private int _generation;
    private IDisposable? _previewSubscription;
    private TaskCompletionSource<CachedBitmapLease>? _previewLease;
    private bool _finalDelivered;
    private bool _disposed;

    internal PresentationImageLoader(
        ImageLoadClient producer,
        RenderBitmapCache cache,
        ID2D1DeviceContext deviceContext,
        IThumbnailImageLoader thumbnails,
        SynchronizationContext uiContext)
        : this(
            producer,
            cache,
            upload => D2DBitmapFactory.Create(deviceContext, upload),
            image => D2DBitmapFactory.Create(deviceContext, image),
            thumbnails,
            uiContext)
    {
    }

    internal PresentationImageLoader(
        ImageLoadClient producer,
        RenderBitmapCache cache,
        Func<DecodedImageUpload, ID2D1Bitmap1> createUploadBitmap,
        Func<DecodedImage, ID2D1Bitmap1> createDecodedBitmap,
        IThumbnailImageLoader thumbnails,
        SynchronizationContext uiContext)
    {
        _producer = producer;
        _cache = cache;
        _createUploadBitmap = createUploadBitmap;
        _createDecodedBitmap = createDecodedBitmap;
        _thumbnails = thumbnails;
        _uiContext = uiContext;
    }

    public void Load(string path, Action<ImageLoadResult> completed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int generation = BeginLoad();
        if (_cache.TryAcquire(path, out CachedBitmapLease? lease))
        {
            _producer.CancelForeground();
            completed(new ImageLoaded(path, new CachedBitmapRepresentation(lease)));
            _cache.Trim();
            return;
        }

        var leaseCompletion = new TaskCompletionSource<CachedBitmapLease>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _previewLease = leaseCompletion;
        _previewSubscription = _thumbnails.Request(
            path,
            ThumbnailPriority.Foreground,
            image =>
            {
                if (!leaseCompletion.TrySetResult(image))
                {
                    image.Dispose();
                }
            });
        _ = DeliverPreviewAsync(generation, path, leaseCompletion.Task, completed);

        _producer.Load(path, result => CompleteForeground(generation, result, completed));
    }

    public void Preload(IEnumerable<string?> paths)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_cache.HasPreloadCapacity)
        {
            _producer.Preload([], CompletePreload);
            return;
        }

        _producer.Preload(
            paths.Where(path => !string.IsNullOrWhiteSpace(path) && !_cache.Contains(path!)),
            CompletePreload);
    }

    private int BeginLoad()
    {
        ResetPreview();
        _finalDelivered = false;
        return ++_generation;
    }

    private void ResetPreview()
    {
        _previewSubscription?.Dispose();
        _previewSubscription = null;
        _previewLease?.TrySetCanceled();
        _previewLease = null;
    }

    private async Task DeliverPreviewAsync(
        int generation,
        string path,
        Task<CachedBitmapLease> thumbnail,
        Action<ImageLoadResult> completed)
    {
        CachedBitmapLease? lease = null;
        try
        {
            lease = await thumbnail.ConfigureAwait(false);
            CachedBitmapLease toDeliver = lease;
            lease = null;
            try
            {
                _uiContext.Post(
                    _ => DeliverPreview(generation, path, toDeliver, completed),
                    null);
            }
            catch
            {
                // Posting failed before delivery; retain ownership locally and release it.
                toDeliver.Dispose();
            }
        }
        catch (Exception)
        {
            lease?.Dispose();
        }
    }

    private void DeliverPreview(
        int generation,
        string path,
        CachedBitmapLease lease,
        Action<ImageLoadResult> completed)
    {
        if (_disposed || generation != _generation || _finalDelivered)
        {
            lease.Dispose();
            return;
        }

        completed(new ImageLoaded(
            path,
            new CachedBitmapRepresentation(lease),
            IsPreview: true));
    }

    private void CompleteForeground(
        int generation,
        ImageLoadResult result,
        Action<ImageLoadResult> completed)
    {
        if (_disposed || generation != _generation)
        {
            DisposeResult(result);
            return;
        }

        _finalDelivered = true;
        ResetPreview();

        if (result is not ImageLoaded loaded)
        {
            completed(result);
            return;
        }

        switch (loaded.Representation)
        {
            case UploadImageRepresentation upload:
                try
                {
                    CompleteUploadedForeground(loaded.Path, upload.Upload, completed);
                }
                finally
                {
                    loaded.Dispose();
                }

                break;

            case DecodedImageRepresentation decoded:
                try
                {
                    CompleteDecodedForeground(loaded.Path, decoded.Image, completed);
                }
                finally
                {
                    loaded.Dispose();
                }

                break;

            default:
                completed(result);
                break;
        }
    }

    private void CompleteUploadedForeground(
        string path,
        DecodedImageUpload upload,
        Action<ImageLoadResult> completed)
    {
        if (_cache.TryAcquire(path, out CachedBitmapLease? existing))
        {
            completed(new ImageLoaded(path, new CachedBitmapRepresentation(existing)));
            _cache.Trim();
            return;
        }

        ID2D1Bitmap1? bitmap = null;
        CachedBitmapLease lease;
        try
        {
            bitmap = _createUploadBitmap(upload);
            lease = _cache.AddAndAcquire(path, bitmap, upload.Width, upload.Height);
            bitmap = null;
        }
        catch (Exception exception)
        {
            Log.Error("Image", $"Could not upload '{Path.GetFileName(path)}' to the renderer.", exception);
            if (bitmap is not null)
            {
                _cache.DisposeUncached(bitmap);
            }

            completed(new ImageLoadFailed(path, exception));
            return;
        }

        completed(new ImageLoaded(path, new CachedBitmapRepresentation(lease)));
        _cache.Trim();
    }

    private void CompleteDecodedForeground(
        string path,
        DecodedImage image,
        Action<ImageLoadResult> completed)
    {
        ID2D1Bitmap1? bitmap = null;
        CachedBitmapLease lease;
        try
        {
            bitmap = _createDecodedBitmap(image);
            lease = _cache.AddAndAcquire(path, bitmap, image.Width, image.Height);
            bitmap = null;
        }
        catch (Exception exception)
        {
            Log.Error("Image", $"Could not create a bitmap for '{Path.GetFileName(path)}'.", exception);
            if (bitmap is not null)
            {
                _cache.DisposeUncached(bitmap);
            }

            completed(new ImageLoadFailed(path, exception));
            return;
        }

        completed(new ImageLoaded(path, new CachedBitmapRepresentation(lease)));
        _cache.Trim();
    }

    private void CompletePreload(ImageLoadResult result)
    {
        if (_disposed)
        {
            DisposeResult(result);
            return;
        }

        if (result is not ImageLoaded
            {
                Representation: UploadImageRepresentation upload,
            } loaded)
        {
            DisposeResult(result);
            return;
        }

        ID2D1Bitmap1? bitmap = null;
        try
        {
            if (_cache.Contains(loaded.Path) || !_cache.CanPreload(upload.Width, upload.Height))
            {
                return;
            }

            bitmap = _createUploadBitmap(upload.Upload);
            _cache.AddInactive(loaded.Path, bitmap, upload.Width, upload.Height);
            bitmap = null;
        }
        catch
        {
            if (bitmap is not null)
            {
                _cache.DisposeUncached(bitmap);
            }

            // Preloading is speculative. Foreground loading will report failures.
        }
        finally
        {
            loaded.Dispose();
        }
    }

    private static void DisposeResult(ImageLoadResult result)
    {
        (result as ImageLoaded)?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ResetPreview();
        _producer.Dispose();
    }
}
