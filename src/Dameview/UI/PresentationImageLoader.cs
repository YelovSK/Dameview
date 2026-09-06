using Dameview.Imaging;
using Vortice.Direct2D1;

namespace Dameview.UI;

// UI-thread facade: checks render resources before asking the background pixel
// producer, and converts temporary uploads into cache-owned Direct2D bitmaps.
internal sealed class PresentationImageLoader : IImageLoader, IDisposable
{
    private readonly ImageLoadCoordinator _producer;
    private readonly RenderBitmapCache _cache;
    private readonly Func<DecodedImageUpload, ID2D1Bitmap1> _createUploadBitmap;
    private readonly Func<DecodedImage, ID2D1Bitmap1> _createDecodedBitmap;
    private bool _disposed;

    internal PresentationImageLoader(
        ImageLoadCoordinator producer,
        RenderBitmapCache cache,
        ID2D1DeviceContext deviceContext)
        : this(
            producer,
            cache,
            upload => D2DBitmapFactory.Create(deviceContext, upload),
            image => D2DBitmapFactory.Create(deviceContext, image))
    {
    }

    internal PresentationImageLoader(
        ImageLoadCoordinator producer,
        RenderBitmapCache cache,
        Func<DecodedImageUpload, ID2D1Bitmap1> createUploadBitmap,
        Func<DecodedImage, ID2D1Bitmap1> createDecodedBitmap)
    {
        _producer = producer;
        _cache = cache;
        _createUploadBitmap = createUploadBitmap;
        _createDecodedBitmap = createDecodedBitmap;
    }

    public void Load(string path, Action<ImageLoadResult> completed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_cache.TryActivate(path, out CachedBitmap? cached))
        {
            _producer.CancelForeground();
            completed(new ImageLoaded(path, new CachedBitmapRepresentation(cached)));
            _cache.Trim();
            return;
        }

        _producer.Load(path, result => CompleteForeground(result, completed));
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

    private void CompleteForeground(
        ImageLoadResult result,
        Action<ImageLoadResult> completed)
    {
        if (_disposed)
        {
            DisposeResult(result);
            return;
        }

        if (result is not ImageLoaded loaded || loaded.IsPreview)
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
        if (_cache.TryActivate(path, out CachedBitmap? existing))
        {
            completed(new ImageLoaded(path, new CachedBitmapRepresentation(existing)));
            _cache.Trim();
            return;
        }

        ID2D1Bitmap1? bitmap = null;
        CachedBitmap cached;
        try
        {
            bitmap = _createUploadBitmap(upload);
            cached = _cache.AddAndActivate(path, bitmap, upload.Width, upload.Height);
            bitmap = null;
        }
        catch (Exception exception)
        {
            if (bitmap is not null)
            {
                _cache.DisposeUncached(bitmap);
            }

            completed(new ImageLoadFailed(path, exception));
            return;
        }

        completed(new ImageLoaded(path, new CachedBitmapRepresentation(cached)));
        _cache.Trim();
    }

    private void CompleteDecodedForeground(
        string path,
        DecodedImage image,
        Action<ImageLoadResult> completed)
    {
        ID2D1Bitmap1? bitmap = null;
        CachedBitmap cached;
        try
        {
            bitmap = _createDecodedBitmap(image);
            cached = _cache.AddAndActivate(path, bitmap, image.Width, image.Height);
            bitmap = null;
        }
        catch (Exception exception)
        {
            if (bitmap is not null)
            {
                _cache.DisposeUncached(bitmap);
            }

            completed(new ImageLoadFailed(path, exception));
            return;
        }

        completed(new ImageLoaded(path, new CachedBitmapRepresentation(cached)));
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
            if (_cache.Contains(loaded.Path) || !_cache.HasPreloadCapacity)
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
        _disposed = true;
    }
}
