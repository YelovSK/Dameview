using System.Diagnostics.CodeAnalysis;
using Dameview.Platform;

namespace Dameview.Imaging;

internal sealed class ImageLoadCoordinator : IDisposable
{
    private readonly object _sync = new();
    private readonly UiPost _postToUi;
    private readonly IImageLoadingBackend _backend;
    private readonly ImageRepresentationPolicy _representationPolicy;
    private readonly Thread _foregroundWorker;
    private readonly Thread _preloadWorker;
    private readonly Queue<PreloadRequest> _pendingPreloads = new();
    private readonly Dictionary<string, InFlightDecode> _inFlightDecodes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IThumbnailLoader _thumbnailLoader;
    private IDisposable? _previewRequest;
    private CancellationTokenSource? _foregroundCancellation;
    private long _completedRequestId;
    private LoadRequest? _pendingRequest;
    private long _latestRequestId;
    private bool _stopping;

    internal ImageLoadCoordinator(
        UiPost postToUi,
        IImageLoadingBackend backend,
        ImageRepresentationPolicy representationPolicy,
        IThumbnailLoader thumbnailLoader)
    {
        _postToUi = postToUi;
        _backend = backend;
        _representationPolicy = representationPolicy;
        _thumbnailLoader = thumbnailLoader;
        _foregroundWorker = CreateWorker("Dameview image loader", ForegroundWork);
        _preloadWorker = CreateWorker("Dameview image preloader", PreloadWork);
        _foregroundWorker.Start();
        _preloadWorker.Start();
    }

    internal void Load(string path, Action<ImageLoadResult> completed)
    {
        LoadRequest request;
        IDisposable? previousPreview;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            _foregroundCancellation?.Cancel();
            _foregroundCancellation?.Dispose();
            _foregroundCancellation = new CancellationTokenSource();
            request = new LoadRequest(
                ++_latestRequestId,
                path,
                completed,
                _foregroundCancellation.Token);
            _pendingRequest = null;
            previousPreview = _previewRequest;
            _previewRequest = null;
        }
        previousPreview?.Dispose();

        bool supportsAnimation = _backend.SupportsAnimation(path);
        IDisposable? previewRequest = supportsAnimation
            ? null
            : _thumbnailLoader.Request(
                path,
                ThumbnailPriority.Foreground,
                image => Deliver(request, new ImageLoaded(
                    request.Path,
                    new DecodedImageRepresentation(image),
                    IsPreview: true)));

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

    internal void Preload(
        IEnumerable<string?> paths,
        Action<ImageLoadResult> completed)
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
                    _pendingPreloads.Enqueue(new PreloadRequest(path, completed));
                }
            }

            Monitor.PulseAll(_sync);
        }
    }

    internal void CancelForeground()
    {
        IDisposable? previewRequest;
        lock (_sync)
        {
            if (_stopping)
            {
                return;
            }

            _foregroundCancellation?.Cancel();
            _pendingRequest = null;
            previewRequest = _previewRequest;
            _previewRequest = null;
            _latestRequestId++;
        }

        previewRequest?.Dispose();
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
            _foregroundCancellation?.Cancel();
            _foregroundCancellation?.Dispose();
            _foregroundCancellation = null;
            _latestRequestId++;
            Monitor.PulseAll(_sync);
        }
        previewRequest?.Dispose();
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
                    result = DecodeForeground(
                        request.Path,
                        decoder!,
                        request.CancellationToken);
                }
                catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
                {
                    continue;
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

    private ImageLoaded DecodeForeground(
        string path,
        IImageDecoder decoder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_backend.SupportsAnimation(path))
        {
            if (RequiresTiledRepresentation(path, decoder))
            {
                IImageTileSource tiledImage = _backend.OpenTiledImage(path);
                return new ImageLoaded(path, new TiledImageRepresentation(tiledImage));
            }

            return new ImageLoaded(
                path,
                new UploadImageRepresentation(DecodeShared(path, decoder, cancellationToken)));
        }

        IAnimationSession? animation = _backend.OpenAnimation(path);
        try
        {
            if (!animation.IsAnimated)
            {
                return new ImageLoaded(
                    path,
                    new DecodedImageRepresentation(animation.FirstFrame.Image));
            }

            var result = new ImageLoaded(path, new AnimatedImageRepresentation(animation));
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
        while (TryTakePreload(out PreloadRequest? request))
        {
            ImageLoadResult? result = null;
            if (initializationError is not null)
            {
                result = new ImageLoadFailed(request.Path, initializationError);
            }
            else if (!_backend.SupportsAnimation(request.Path))
            {
                try
                {
                    if (!RequiresTiledRepresentation(request.Path, decoder!))
                    {
                        result = new ImageLoaded(
                            request.Path,
                            new UploadImageRepresentation(DecodeShared(
                                request.Path,
                                decoder!,
                                CancellationToken.None)));
                    }
                }
                catch (Exception exception)
                {
                    result = new ImageLoadFailed(request.Path, exception);
                }
            }

            if (result is not null)
            {
                ImageLoadResult delivered = result;
                _postToUi(() => request.Completed(delivered));
            }
        }
    }

    private bool RequiresTiledRepresentation(string path, IImageDecoder decoder)
    {
        return _representationPolicy.RequiresTiling(decoder.GetInfo(path));
    }

    private DecodedImageUpload DecodeShared(
        string path,
        IImageDecoder decoder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InFlightDecode pending;
        bool ownsDecode;

        lock (_sync)
        {
            ownsDecode = !_inFlightDecodes.TryGetValue(path, out pending!);
            if (ownsDecode)
            {
                pending = new InFlightDecode();
                _inFlightDecodes.Add(path, pending);
            }

            pending.AddConsumer();
        }

        if (ownsDecode)
        {
            try
            {
                DecodedImageUpload upload = decoder.DecodeUpload(path, cancellationToken);
                pending.Completion.SetResult(upload);
            }
            catch (Exception exception)
            {
                pending.Completion.SetException(exception);
            }
        }

        try
        {
            DecodedImageUpload shared = pending.Completion.Task.GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();
            return shared.Retain(() => ReleaseConsumer(path, pending));
        }
        catch
        {
            ReleaseConsumer(path, pending);
            throw;
        }
    }

    private void ReleaseConsumer(string path, InFlightDecode pending)
    {
        DecodedImageUpload? owner = null;
        lock (_sync)
        {
            if (pending.ReleaseConsumer() == 0)
            {
                _inFlightDecodes.Remove(path);
                if (pending.Completion.Task.IsCompletedSuccessfully)
                {
                    owner = pending.Completion.Task.Result;
                }
            }
        }

        owner?.Dispose();
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

    private bool TryTakePreload([NotNullWhen(true)] out PreloadRequest? request)
    {
        lock (_sync)
        {
            while (!_stopping && _pendingPreloads.Count == 0)
            {
                Monitor.Wait(_sync);
            }

            if (_stopping)
            {
                request = null;
                return false;
            }

            request = _pendingPreloads.Dequeue();
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
        (result as ImageLoaded)?.Dispose();
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
        Action<ImageLoadResult> Completed,
        CancellationToken CancellationToken);

    private sealed record PreloadRequest(
        string Path,
        Action<ImageLoadResult> Completed);

    private sealed class InFlightDecode
    {
        internal TaskCompletionSource<DecodedImageUpload> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int ConsumerCount { get; private set; }

        internal void AddConsumer() => ConsumerCount++;
        internal int ReleaseConsumer() => --ConsumerCount;
    }
}

internal abstract record ImageLoadResult(string Path);

// Ownership transfers to the receiver when this result is delivered.
internal sealed record ImageLoaded(
    string Path,
    ImageRepresentation Representation,
    bool IsPreview = false) : ImageLoadResult(Path), IDisposable
{
    public void Dispose() => Representation.Dispose();
}

internal sealed record ImageLoadFailed(
    string Path,
    Exception Exception) : ImageLoadResult(Path);
