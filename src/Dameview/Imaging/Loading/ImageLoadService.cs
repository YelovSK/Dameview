using Dameview.Platform;

namespace Dameview.Imaging;

internal sealed class ImageLoadService : IDisposable
{
    private readonly Lock _sync = new();
    private readonly SynchronizationContext _uiContext;
    private readonly IImageLoadingBackend _backend;
    private readonly ImageRepresentationPolicy _representationPolicy;
    private readonly IThumbnailLoader _thumbnailLoader;
    private readonly IImageInfoLoader _imageInfoLoader;
    private readonly ComWorkerQueue<IImageDecoder> _foregroundQueue;
    private readonly ComWorkerQueue<IImageDecoder> _preloadQueue;
    private readonly Dictionary<ImageLoadClient, ClientState> _clients = [];
    private readonly Dictionary<string, InFlightDecode> _inFlightDecodes =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _stopping;

    internal ImageLoadService(
        SynchronizationContext uiContext,
        IImageLoadingBackend backend,
        ImageRepresentationPolicy representationPolicy,
        IThumbnailLoader thumbnailLoader,
        IImageInfoLoader imageInfoLoader)
    {
        _uiContext = uiContext;
        _backend = backend;
        _representationPolicy = representationPolicy;
        _thumbnailLoader = thumbnailLoader;
        _imageInfoLoader = imageInfoLoader;
        _foregroundQueue = new ComWorkerQueue<IImageDecoder>(
            "Dameview image loader",
            2,
            _backend.CreateDecoder);
        _preloadQueue = new ComWorkerQueue<IImageDecoder>(
            "Dameview image preloader",
            1,
            _backend.CreateDecoder);
    }

    internal ImageLoadClient CreateClient()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            var client = new ImageLoadClient(this);
            _clients.Add(client, new ClientState());
            return client;
        }
    }

    public void Dispose()
    {
        List<IDisposable> previews = [];
        lock (_sync)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            foreach (ClientState state in _clients.Values)
            {
                CancelLoad(state);
                if (state.PreviewRequest is { } preview)
                {
                    previews.Add(preview);
                }
            }

            _clients.Clear();
        }

        foreach (IDisposable preview in previews)
        {
            preview.Dispose();
        }

        _foregroundQueue.Dispose();
        _preloadQueue.Dispose();
    }

    // Must be called on the UI thread, serialized per client. The request, its preview
    // subscription, and the scheduled work are swapped across two critical sections because the
    // preview has to be registered before the decode can be started; a concurrent caller for the
    // same client would break that ordering.
    internal void Load(ImageLoadClient client, string path, Action<ImageLoadResult> completed)
    {
        bool supportsAnimation = _backend.SupportsAnimation(path);

        LoadRequest request;
        IDisposable? previousPreview;
        lock (_sync)
        {
            ClientState state = GetState(client);
            CancelLoad(state);
            previousPreview = state.PreviewRequest;
            state.PreviewRequest = null;
            var cancellation = new CancellationTokenSource();
            request = supportsAnimation
                ? new AnimatedLoadRequest(client, path, completed, cancellation)
                : new StaticLoadRequest(
                    client,
                    path,
                    completed,
                    cancellation,
                    _imageInfoLoader.LoadAsync(path, cancellation.Token));
            state.CurrentLoad = request;
            state.PendingLoad = request;
        }

        previousPreview?.Dispose();

        // Both preview inputs are started here, off the decode workers, so fast navigation does
        // not have to wait for a worker stuck on an in-flight (non-cancellable) decode.
        IDisposable? previewRequest = null;
        if (request is StaticLoadRequest staticRequest)
        {
            var thumbnail = new TaskCompletionSource<DecodedImage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            previewRequest = _thumbnailLoader.Request(
                path,
                ThumbnailPriority.Foreground,
                image => thumbnail.TrySetResult(image));
            _ = DeliverPreviewAsync(staticRequest, thumbnail.Task);
        }

        bool accepted;
        LoadRequest? start = null;
        lock (_sync)
        {
            accepted = IsCurrentUnsafe(request);
            if (accepted)
            {
                ClientState state = _clients[request.Client];
                state.PreviewRequest = previewRequest;
                start = TakeActive(state);
            }
        }

        if (!accepted)
        {
            previewRequest?.Dispose();
        }
        else if (start is not null)
        {
            StartForeground(start);
        }
    }

    internal void Preload(
        ImageLoadClient client,
        IEnumerable<string?> paths,
        Action<ImageLoadResult> completed)
    {
        List<PreloadRequest> requests = [];
        lock (_sync)
        {
            ClientState state = GetState(client);
            int generation = ++state.PreloadGeneration;
            var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string? path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path) && uniquePaths.Add(path))
                {
                    requests.Add(new PreloadRequest(client, generation, path, completed));
                }
            }
        }

        foreach (PreloadRequest request in requests)
        {
            _ = ProcessPreloadAsync(request);
        }
    }

    internal void CancelForeground(ImageLoadClient client)
    {
        IDisposable? preview;
        lock (_sync)
        {
            ClientState state = GetState(client);
            CancelLoad(state);
            state.CurrentLoad = null;
            state.PendingLoad = null;
            preview = state.PreviewRequest;
            state.PreviewRequest = null;
        }

        preview?.Dispose();
    }

    internal void RemoveClient(ImageLoadClient client)
    {
        IDisposable? preview = null;
        lock (_sync)
        {
            if (_clients.Remove(client, out ClientState? state))
            {
                CancelLoad(state);
                preview = state.PreviewRequest;
            }
        }

        preview?.Dispose();
    }

    private ClientState GetState(ImageLoadClient client)
    {
        ObjectDisposedException.ThrowIf(_stopping, this);
        return _clients.TryGetValue(client, out ClientState? state)
            ? state
            : throw new ObjectDisposedException(nameof(ImageLoadClient));
    }

    private static void CancelLoad(ClientState state)
    {
        state.CurrentLoad?.Cancellation.Cancel();
        state.CurrentLoad?.Cancellation.Dispose();
    }

    // The caller must hold _sync.
    private static LoadRequest? TakeActive(ClientState state)
    {
        if (state.LoadActive || state.PendingLoad is null)
        {
            return null;
        }

        state.LoadActive = true;
        LoadRequest request = state.PendingLoad;
        state.PendingLoad = null;
        return request;
    }

    private void StartForeground(LoadRequest request) => _ = ProcessForegroundAsync(request);

    private async Task ProcessForegroundAsync(LoadRequest request)
    {
        ImageLoadResult result;
        try
        {
            result = await _foregroundQueue.Enqueue(
                (decoder, token) => DecodeResult(request, decoder, token),
                cancellationToken: request.Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested)
        {
            FinishLoad(request);
            return;
        }
        catch (Exception exception)
        {
            result = new ImageLoadFailed(request.Path, exception);
        }

        PostToUi(() => DeliverForeground(request, result));
        FinishLoad(request);
    }

    private ImageLoadResult DecodeResult(
        LoadRequest request,
        IImageDecoder decoder,
        CancellationToken cancellationToken)
    {
        try
        {
            return DecodeForeground(request, decoder, cancellationToken);
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ImageLoadFailed(request.Path, exception);
        }
    }

    private void DeliverForeground(LoadRequest request, ImageLoadResult result)
    {
        if (IsCurrent(request))
        {
            Deliver(request, result);
        }
        else
        {
            DisposeResult(result);
        }
    }

    private async Task ProcessPreloadAsync(PreloadRequest request)
    {
        ImageLoadResult? result = null;
        try
        {
            if (IsPreloadCurrent(request) && !_backend.SupportsAnimation(request.Path))
            {
                ImageInfo info = await _imageInfoLoader
                    .LoadAsync(request.Path, CancellationToken.None)
                    .ConfigureAwait(false);
                if (!_representationPolicy.RequiresTiling(info))
                {
                    result = await _preloadQueue.Enqueue(
                        (decoder, _) => PreloadDecode(request, decoder)).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            result = new ImageLoadFailed(request.Path, exception);
        }

        if (result is not null)
        {
            PostToUi(() => DeliverPreload(request, result));
        }
    }

    private ImageLoadResult? PreloadDecode(PreloadRequest request, IImageDecoder decoder)
    {
        if (!IsPreloadCurrent(request))
        {
            return null;
        }

        try
        {
            return new ImageLoaded(
                request.Path,
                new UploadImageRepresentation(DecodeShared(
                    request.Path,
                    decoder,
                    CancellationToken.None)));
        }
        catch (Exception exception)
        {
            return new ImageLoadFailed(request.Path, exception);
        }
    }

    private bool IsPreloadCurrent(PreloadRequest request)
    {
        lock (_sync)
        {
            return _clients.TryGetValue(request.Client, out ClientState? state)
                && request.Generation == state.PreloadGeneration;
        }
    }

    private ImageLoaded DecodeForeground(
        LoadRequest request,
        IImageDecoder decoder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request is StaticLoadRequest staticRequest)
        {
            ImageInfo sourceInfo = staticRequest.SourceInfo.GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();
            if (_representationPolicy.RequiresTiling(sourceInfo))
            {
                IImageTileSource tiledImage = _backend.OpenTiledImage(request.Path);
                return new ImageLoaded(request.Path, new TiledImageRepresentation(tiledImage));
            }

            return new ImageLoaded(
                request.Path,
                new UploadImageRepresentation(DecodeShared(request.Path, decoder, cancellationToken)));
        }

        IAnimationSession? animation = _backend.OpenAnimation(request.Path);
        try
        {
            if (!animation.IsAnimated)
            {
                return new ImageLoaded(
                    request.Path,
                    new DecodedImageRepresentation(animation.FirstFrame.Image));
            }

            var result = new ImageLoaded(request.Path, new AnimatedImageRepresentation(animation));
            animation = null;
            return result;
        }
        finally
        {
            animation?.Dispose();
        }
    }

    private void DeliverPreview(StaticLoadRequest request, PreviewImage preview)
    {
        Deliver(request, new ImageLoaded(
            request.Path,
            new DecodedImageRepresentation(preview.Image, preview.SourceInfo),
            IsPreview: true));
    }

    // A preview is the join of two independent async inputs: the thumbnail pixels and the
    // source dimensions. It is best-effort, so either input failing simply yields no preview;
    // a stale preview is dropped by Deliver's currency check.
    private async Task DeliverPreviewAsync(StaticLoadRequest request, Task<DecodedImage> thumbnail)
    {
        try
        {
            await Task.WhenAll(thumbnail, request.SourceInfo).ConfigureAwait(false);
            DecodedImage image = thumbnail.Result;
            ImageInfo display = GetDisplayInfo(request.SourceInfo.Result, image);
            var preview = new PreviewImage(image, display);
            PostToUi(() => DeliverPreview(request, preview));
        }
        catch (Exception)
        {
        }
    }

    // GetInfo returns the stored (unrotated) dimensions while the thumbnail is orientation-applied.
    // Infer a 90/270 swap when their aspect orientations disagree, so the preview matches the image.
    private static ImageInfo GetDisplayInfo(ImageInfo source, DecodedImage thumbnail)
    {
        return (source.Width >= source.Height) == (thumbnail.Width >= thumbnail.Height)
            ? source
            : new ImageInfo(source.Height, source.Width);
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

    private void FinishLoad(LoadRequest request)
    {
        LoadRequest? next = null;
        lock (_sync)
        {
            if (!_clients.TryGetValue(request.Client, out ClientState? state))
            {
                return;
            }

            state.LoadActive = false;
            next = TakeActive(state);
        }

        if (next is not null)
        {
            StartForeground(next);
        }
    }

    private bool IsCurrent(LoadRequest request)
    {
        lock (_sync)
        {
            return IsCurrentUnsafe(request);
        }
    }

    // The caller must hold _sync.
    private bool IsCurrentUnsafe(LoadRequest request) =>
        !_stopping
        && _clients.TryGetValue(request.Client, out ClientState? state)
        && ReferenceEquals(state.CurrentLoad, request)
        && !request.IsComplete;

    private void Deliver(LoadRequest request, ImageLoadResult result)
    {
        bool accepted;
        IDisposable? preview = null;
        lock (_sync)
        {
            accepted = IsCurrentUnsafe(request);
            if (accepted && result is not ImageLoaded { IsPreview: true })
            {
                request.IsComplete = true;
                ClientState state = _clients[request.Client];
                preview = state.PreviewRequest;
                state.PreviewRequest = null;
            }
        }

        preview?.Dispose();
        if (accepted)
        {
            request.Completed(result);
        }
        else
        {
            DisposeResult(result);
        }
    }

    private void DeliverPreload(PreloadRequest request, ImageLoadResult result)
    {
        bool accepted;
        lock (_sync)
        {
            accepted = !_stopping && _clients.ContainsKey(request.Client);
        }

        if (accepted)
        {
            request.Completed(result);
        }
        else
        {
            DisposeResult(result);
        }
    }

    private static void DisposeResult(ImageLoadResult result)
    {
        (result as ImageLoaded)?.Dispose();
    }

    private void PostToUi(Action action)
    {
        _uiContext.Post(_ => action(), null);
    }

    private sealed class ClientState
    {
        internal LoadRequest? CurrentLoad { get; set; }
        internal LoadRequest? PendingLoad { get; set; }
        internal IDisposable? PreviewRequest { get; set; }
        internal int PreloadGeneration { get; set; }
        internal bool LoadActive { get; set; }
    }

    private abstract class LoadRequest(
        ImageLoadClient client,
        string path,
        Action<ImageLoadResult> completed,
        CancellationTokenSource cancellation)
    {
        internal ImageLoadClient Client { get; } = client;
        internal string Path { get; } = path;
        internal Action<ImageLoadResult> Completed { get; } = completed;
        internal CancellationTokenSource Cancellation { get; } = cancellation;
        internal bool IsComplete { get; set; }
    }

    private sealed class AnimatedLoadRequest(
        ImageLoadClient client,
        string path,
        Action<ImageLoadResult> completed,
        CancellationTokenSource cancellation)
        : LoadRequest(client, path, completed, cancellation)
    {
    }

    private sealed class StaticLoadRequest(
        ImageLoadClient client,
        string path,
        Action<ImageLoadResult> completed,
        CancellationTokenSource cancellation,
        Task<ImageInfo> sourceInfo)
        : LoadRequest(client, path, completed, cancellation)
    {
        internal Task<ImageInfo> SourceInfo { get; } = sourceInfo;
    }

    private readonly record struct PreviewImage(DecodedImage Image, ImageInfo SourceInfo);

    private sealed record PreloadRequest(
        ImageLoadClient Client,
        int Generation,
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

internal sealed class ImageLoadClient : IDisposable
{
    private ImageLoadService? _service;

    internal ImageLoadClient(ImageLoadService service)
    {
        _service = service;
    }

    internal void Load(string path, Action<ImageLoadResult> completed)
        => Service.Load(this, path, completed);

    internal void Preload(IEnumerable<string?> paths, Action<ImageLoadResult> completed)
        => Service.Preload(this, paths, completed);

    internal void CancelForeground() => Service.CancelForeground(this);

    public void Dispose()
    {
        ImageLoadService? service = _service;
        _service = null;
        service?.RemoveClient(this);
    }

    private ImageLoadService Service => _service
        ?? throw new ObjectDisposedException(nameof(ImageLoadClient));
}
