using Dameview.Diagnostics;
using Dameview.Imaging.Loading;
using Dameview.Rendering;
using Vortice.Direct2D1;

namespace Dameview.UI.Presentation;

// UI-thread facade: checks render resources before asking the background pixel
// producer, turns temporary uploads into cache-owned Direct2D bitmaps, and presents GPU
// thumbnails while the full image loads.
internal sealed class PresentationImageLoader : IImageLoader
{
    private readonly ImageLoadClient _producer;
    private readonly RenderBitmapCache _cache;
    private readonly IThumbnailImageLoader _thumbnails;
    private readonly Func<ImageRepresentation, Task<ID2D1Bitmap1>> _createBitmap;
    // So that a preload and a foreground load of the same file share one upload.
    private readonly HashSet<string> _uploading = new(StringComparer.OrdinalIgnoreCase);
    // Disposing it is what keeps a preview from arriving after the full image or a newer load.
    private IDisposable? _previewSubscription;
    // The image to show once its upload finishes. A newer load clears it, so an upload that
    // finishes too late only ends up in the cache.
    private (string Path, Action<ImageLoadResult> Completed)? _waiting;
    private bool _disposed;

    internal PresentationImageLoader(
        ImageLoadClient producer,
        RenderBitmapCache cache,
        BitmapUploader uploader,
        IThumbnailImageLoader thumbnails)
        : this(producer, cache, uploader.CreateAsync, thumbnails)
    {
    }

    /// <param name="createBitmap">
    /// Called on the window thread with a static image, and its task has to complete there too.
    /// </param>
    internal PresentationImageLoader(
        ImageLoadClient producer,
        RenderBitmapCache cache,
        Func<ImageRepresentation, Task<ID2D1Bitmap1>> createBitmap,
        IThumbnailImageLoader thumbnails)
    {
        _producer = producer;
        _cache = cache;
        _createBitmap = createBitmap;
        _thumbnails = thumbnails;
    }

    public void Load(string path, Action<ImageLoadResult> completed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _waiting = null;
        ResetPreview();
        if (_cache.TryAcquire(path, out CachedBitmapLease? lease))
        {
            _producer.CancelForeground();
            completed(new ImageLoaded(path, lease));
            return;
        }

        _previewSubscription = _thumbnails.Request(
            path,
            ThumbnailPriority.Foreground,
            preview => completed(new ImageLoaded(path, preview, IsPreview: true)));
        _producer.Load(path, result => CompleteForeground(result, completed));
    }

    public void Preload(IEnumerable<string?> paths)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _producer.Preload(
            paths.Where(path => !string.IsNullOrWhiteSpace(path)
                && !_cache.Contains(path!)
                && !_uploading.Contains(path!)),
            _cache.FreeBytes,
            CompletePreload);
    }

    private void ResetPreview()
    {
        _previewSubscription?.Dispose();
        _previewSubscription = null;
    }

    private void CompleteForeground(ImageLoadResult result, Action<ImageLoadResult> completed)
    {
        ResetPreview();
        if (result is not ImageLoaded { Representation: UploadImageRepresentation or DecodedImageRepresentation } loaded)
        {
            completed(result);
            return;
        }

        // A preload of the same file may have finished uploading since the load started.
        if (_cache.TryAcquire(loaded.Path, out CachedBitmapLease? lease))
        {
            loaded.Dispose();
            completed(new ImageLoaded(loaded.Path, lease));
            return;
        }

        _waiting = (loaded.Path, completed);
        Upload(loaded);
    }

    private void CompletePreload(ImageLoadResult result)
    {
        if (result is ImageLoaded { Representation: UploadImageRepresentation } loaded
            && !_cache.Contains(loaded.Path))
        {
            Upload(loaded);
        }
        else
        {
            (result as ImageLoaded)?.Dispose();
        }
    }

    private void Upload(ImageLoaded loaded)
    {
        if (_uploading.Contains(loaded.Path))
        {
            loaded.Dispose();
            return;
        }

        _ = UploadAsync(loaded);
    }

    private async Task UploadAsync(ImageLoaded loaded)
    {
        string path = loaded.Path;
        ImageRepresentation image = loaded.Representation;
        _uploading.Add(path);
        ID2D1Bitmap1 bitmap;
        try
        {
            using (loaded)
            {
                bitmap = await _createBitmap(image).ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            _uploading.Remove(path);
            // Failed preloads stay quiet. Loading the image for real will report the failure.
            if (TakeWaiting(path) is { } failed)
            {
                Log.Error("Image", $"Could not upload '{Path.GetFileName(path)}' to the renderer.", exception);
                failed(new ImageLoadFailed(path, exception));
            }

            return;
        }

        _uploading.Remove(path);
        if (_disposed)
        {
            bitmap.Dispose();
            return;
        }

        if (TakeWaiting(path) is not { } completed)
        {
            if (!_cache.TryPreload(path, image.Width, image.Height, image.Orientation, () => bitmap))
            {
                bitmap.Dispose();
            }

            return;
        }

        CachedBitmapLease lease = _cache.GetOrAdd(
            path,
            image.Width,
            image.Height,
            image.Orientation,
            () => bitmap);
        // Another tab can upload the same file at the same time.
        if (lease.Bitmap != bitmap)
        {
            bitmap.Dispose();
        }

        completed(new ImageLoaded(path, lease));
        _cache.Trim();
    }

    private Action<ImageLoadResult>? TakeWaiting(string path)
    {
        if (_waiting is not { } waiting || !string.Equals(waiting.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        _waiting = null;
        return waiting.Completed;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _waiting = null;
        ResetPreview();
        _producer.Dispose();
    }
}
