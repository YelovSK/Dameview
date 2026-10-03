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
    private readonly Func<DecodedImageUpload, ID2D1Bitmap1> _createUploadBitmap;
    private readonly Func<DecodedImage, ID2D1Bitmap1> _createDecodedBitmap;
    // Disposing it is what keeps a preview from arriving after the full image or a newer load.
    private IDisposable? _previewSubscription;
    private bool _disposed;

    internal PresentationImageLoader(
        ImageLoadClient producer,
        RenderBitmapCache cache,
        Func<ID2D1DeviceContext> deviceContext,
        IThumbnailImageLoader thumbnails)
        : this(
            producer,
            cache,
            // Resolved per upload, since the device can be replaced.
            upload => D2DBitmapFactory.Create(deviceContext(), upload),
            image => D2DBitmapFactory.Create(deviceContext(), image),
            thumbnails)
    {
    }

    internal PresentationImageLoader(
        ImageLoadClient producer,
        RenderBitmapCache cache,
        Func<DecodedImageUpload, ID2D1Bitmap1> createUploadBitmap,
        Func<DecodedImage, ID2D1Bitmap1> createDecodedBitmap,
        IThumbnailImageLoader thumbnails)
    {
        _producer = producer;
        _cache = cache;
        _createUploadBitmap = createUploadBitmap;
        _createDecodedBitmap = createDecodedBitmap;
        _thumbnails = thumbnails;
    }

    public void Load(string path, Action<ImageLoadResult> completed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
            paths.Where(path => !string.IsNullOrWhiteSpace(path) && !_cache.Contains(path!)),
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
        Func<ID2D1Bitmap1>? create = (result as ImageLoaded)?.Representation switch
        {
            UploadImageRepresentation upload => () => _createUploadBitmap(upload.Upload),
            DecodedImageRepresentation decoded => () => _createDecodedBitmap(decoded.Image),
            _ => null,
        };
        if (create is null)
        {
            completed(result);
            return;
        }

        var loaded = (ImageLoaded)result;
        CachedBitmapLease lease;
        using (loaded)
        {
            ImageRepresentation image = loaded.Representation;
            try
            {
                lease = _cache.GetOrAdd(loaded.Path, image.Width, image.Height, image.Orientation, create);
            }
            catch (Exception exception)
            {
                Log.Error("Image", $"Could not upload '{Path.GetFileName(loaded.Path)}' to the renderer.", exception);
                completed(new ImageLoadFailed(loaded.Path, exception));
                return;
            }
        }

        completed(new ImageLoaded(loaded.Path, lease));
        _cache.Trim();
    }

    private void CompletePreload(ImageLoadResult result)
    {
        if (result is not ImageLoaded { Representation: UploadImageRepresentation upload } loaded)
        {
            (result as ImageLoaded)?.Dispose();
            return;
        }

        using (loaded)
        {
            try
            {
                _cache.TryPreload(
                    loaded.Path,
                    upload.Width,
                    upload.Height,
                    upload.Orientation,
                    () => _createUploadBitmap(upload.Upload));
            }
            catch
            {
                // Preloading is speculative. Foreground loading will report failures.
            }
        }
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
