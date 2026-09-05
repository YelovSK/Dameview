using System.Diagnostics.CodeAnalysis;
using Dameview.Platform;

namespace Dameview.Imaging;

internal sealed class ImageLoadCoordinator : IImageLoader, IDisposable
{
    private const long DefaultCacheCapacityBytes = 256L * 1024L * 1024L;

    private readonly object _sync = new();
    private readonly Action<Action> _postToUi;
    private readonly IImageLoadingBackend _backend;
    private readonly DecodedImageCache _cache;
    private readonly Thread _foregroundWorker;
    private readonly Thread _preloadWorker;
    private readonly Queue<string> _pendingPreloads = new();
    private readonly Dictionary<string, TaskCompletionSource<DecodedImage>> _inFlightDecodes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IThumbnailLoader _thumbnailLoader;
    private readonly ThumbnailCoordinator? _ownedThumbnailLoader;
    private IDisposable? _previewRequest;
    private long _completedRequestId;
    private LoadRequest? _pendingRequest;
    private long _latestRequestId;
    private bool _stopping;

    internal ImageLoadCoordinator(
        Action<Action> postToUi,
        IImageLoadingBackend backend,
        long cacheCapacityBytes = DefaultCacheCapacityBytes,
        IThumbnailLoader? thumbnailLoader = null)
    {
        _postToUi = postToUi;
        _backend = backend;
        _cache = new DecodedImageCache(cacheCapacityBytes);
        _ownedThumbnailLoader = thumbnailLoader is null
            ? new ThumbnailCoordinator(postToUi, backend.LoadThumbnail)
            : null;
        _thumbnailLoader = thumbnailLoader ?? _ownedThumbnailLoader!;
        _foregroundWorker = CreateWorker("Dameview image loader", ForegroundWork);
        _preloadWorker = CreateWorker("Dameview image preloader", PreloadWork);
        _foregroundWorker.Start();
        _preloadWorker.Start();
    }

    public void Load(string path, Action<ImageLoadResult> completed)
    {
        LoadRequest request;
        IDisposable? previousPreview;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            request = new LoadRequest(++_latestRequestId, path, completed);
            _pendingRequest = null;
            previousPreview = _previewRequest;
            _previewRequest = null;
        }
        previousPreview?.Dispose();

        bool supportsAnimation = _backend.SupportsAnimation(path);
        if (!supportsAnimation && _cache.TryGet(path, out DecodedImage? cachedImage))
        {
            var result = new ImageLoaded(path, cachedImage);
            _postToUi(() => Deliver(request, result));
            return;
        }

        IDisposable? previewRequest = supportsAnimation
            ? null
            : _thumbnailLoader.Request(
                path,
                ThumbnailPriority.Foreground,
                image => Deliver(request, new ImageLoaded(request.Path, image, IsPreview: true)));

        lock (_sync)
        {
            if (!_stopping && request.Id == _latestRequestId)
            {
                _pendingRequest = request;
                _previewRequest = previewRequest;
                Monitor.PulseAll(_sync);
            }
            else
            {
                previewRequest?.Dispose();
            }
        }
    }

    public void Preload(IEnumerable<string?> paths)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            _pendingPreloads.Clear();

            var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string? path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path) && uniquePaths.Add(path))
                {
                    _pendingPreloads.Enqueue(path);
                }
            }

            Monitor.PulseAll(_sync);
        }
    }

    public void Dispose()
    {
        IDisposable? previewRequest;
        lock (_sync)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            previewRequest = _previewRequest;
            _previewRequest = null;
            _pendingRequest = null;
            _pendingPreloads.Clear();
            _latestRequestId++;
            Monitor.PulseAll(_sync);
        }
        previewRequest?.Dispose();
        _ownedThumbnailLoader?.Dispose();
    }

    private Thread CreateWorker(string name, Action<IImageDecoder?, Exception?> work)
    {
        return new Thread(() => RunWorker(work))
        {
            IsBackground = true,
            Name = name,
        };
    }

    private void RunWorker(Action<IImageDecoder?, Exception?> work)
    {
        bool comInitialized = false;
        IImageDecoder? decoder = null;
        Exception? initializationError = null;

        try
        {
            try
            {
                NativeMethods.InitializeComApartment(ComApartment.MultiThreaded);
                comInitialized = true;
                decoder = _backend.CreateDecoder();
            }
            catch (Exception exception)
            {
                initializationError = exception;
            }

            work(decoder, initializationError);
        }
        finally
        {
            decoder?.Dispose();
            if (comInitialized)
            {
                NativeMethods.UninitializeComApartment();
            }
        }
    }

    private void ForegroundWork(IImageDecoder? decoder, Exception? initializationError)
    {
        while (TryTakeRequest(out LoadRequest? request))
        {
            ImageLoadResult result;
            if (initializationError is not null)
            {
                result = new ImageLoadFailed(request.Path, initializationError);
            }
            else
            {
                try
                {
                    result = DecodeForeground(request.Path, decoder!);
                }
                catch (Exception exception)
                {
                    result = new ImageLoadFailed(request.Path, exception);
                }
            }

            if (IsLatest(request.Id))
            {
                try
                {
                    _postToUi(() => Deliver(request, result));
                }
                catch
                {
                    DisposeResult(result);
                    throw;
                }
            }
            else
            {
                DisposeResult(result);
            }
        }
    }

    private ImageLoaded DecodeForeground(string path, IImageDecoder decoder)
    {
        if (!_backend.SupportsAnimation(path))
        {
            return new ImageLoaded(path, DecodeShared(path, decoder));
        }

        IAnimationSession? animation = _backend.OpenAnimation(path);
        try
        {
            if (!animation.IsAnimated)
            {
                return new ImageLoaded(path, animation.FirstFrame.Image);
            }

            var result = new ImageLoaded(path, animation.FirstFrame.Image, Animation: animation);
            animation = null;
            return result;
        }
        finally
        {
            animation?.Dispose();
        }
    }

    private void PreloadWork(IImageDecoder? decoder, Exception? initializationError)
    {
        while (TryTakePreload(out string? path))
        {
            if (initializationError is not null || _backend.SupportsAnimation(path) || _cache.TryGet(path, out _))
            {
                continue;
            }

            try
            {
                _ = DecodeShared(path, decoder!);
            }
            catch (Exception)
            {
                // Preloading is speculative. A foreground load will report failures.
            }
        }
    }

    private DecodedImage DecodeShared(string path, IImageDecoder decoder)
    {
        if (_cache.TryGet(path, out DecodedImage? cachedImage))
        {
            return cachedImage;
        }

        TaskCompletionSource<DecodedImage> completion;
        bool ownsDecode;

        lock (_sync)
        {
            ownsDecode = !_inFlightDecodes.TryGetValue(path, out completion!);
            if (ownsDecode)
            {
                completion = new TaskCompletionSource<DecodedImage>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _inFlightDecodes.Add(path, completion);
            }
        }

        if (!ownsDecode)
        {
            return completion.Task.GetAwaiter().GetResult();
        }

        try
        {
            DecodedImage image = decoder.Decode(path);
            _cache.Add(path, image);
            completion.SetResult(image);
            return image;
        }
        catch (Exception exception)
        {
            completion.SetException(exception);
            return completion.Task.GetAwaiter().GetResult();
        }
        finally
        {
            lock (_sync)
            {
                _inFlightDecodes.Remove(path);
            }
        }
    }

    private bool TryTakeRequest([NotNullWhen(true)] out LoadRequest? request)
    {
        lock (_sync)
        {
            while (!_stopping && _pendingRequest is null)
            {
                Monitor.Wait(_sync);
            }

            if (_stopping)
            {
                request = null;
                return false;
            }

            request = _pendingRequest!;
            _pendingRequest = null;
            return true;
        }
    }

    private bool TryTakePreload([NotNullWhen(true)] out string? path)
    {
        lock (_sync)
        {
            while (!_stopping && _pendingPreloads.Count == 0)
            {
                Monitor.Wait(_sync);
            }

            if (_stopping)
            {
                path = null;
                return false;
            }

            path = _pendingPreloads.Dequeue();
            return true;
        }
    }

    private void Deliver(LoadRequest request, ImageLoadResult result)
    {
        bool accepted;
        lock (_sync)
        {
            accepted = !_stopping && request.Id == _latestRequestId && request.Id > _completedRequestId;
            if (accepted && result is not ImageLoaded { IsPreview: true })
            {
                _completedRequestId = request.Id;
            }
        }

        if (!accepted)
        {
            DisposeResult(result);
            return;
        }

        request.Completed(result);
    }

    private static void DisposeResult(ImageLoadResult result)
    {
        if (result is ImageLoaded { Animation: { } animation })
        {
            animation.Dispose();
        }
    }

    private bool IsLatest(long requestId)
    {
        lock (_sync)
        {
            return !_stopping && requestId == _latestRequestId;
        }
    }

    private sealed record LoadRequest(
        long Id,
        string Path,
        Action<ImageLoadResult> Completed);
}

internal abstract record ImageLoadResult(string Path);

internal sealed record ImageLoaded(
    string Path,
    DecodedImage Image,
    bool IsPreview = false,
    // The receiver owns this session once the result is delivered.
    IAnimationSession? Animation = null) : ImageLoadResult(Path);

internal sealed record ImageLoadFailed(
    string Path,
    Exception Exception) : ImageLoadResult(Path);
