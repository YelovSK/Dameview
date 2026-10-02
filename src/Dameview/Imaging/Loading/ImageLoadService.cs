using Dameview.Diagnostics;
using Dameview.Imaging.Animation;
using Dameview.Imaging.Decoding;
using Dameview.Win32;

namespace Dameview.Imaging.Loading;

internal sealed class ImageLoadService : IDisposable
{
    private readonly Lock _sync = new();
    private readonly SynchronizationContext _uiContext;
    private readonly IImageLoadingBackend _backend;
    private readonly ImageRepresentationPolicy _representationPolicy;
    private readonly ComWorkerQueue<IImageDecoder> _foregroundQueue;
    private readonly ComWorkerQueue<IImageDecoder> _preloadQueue;
    private readonly Dictionary<ImageLoadClient, ClientState> _clients = [];
    private readonly Dictionary<string, InFlightDecode> _inFlightDecodes =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _stopping;

    internal ImageLoadService(
        SynchronizationContext uiContext,
        IImageLoadingBackend backend,
        ImageRepresentationPolicy representationPolicy)
    {
        _uiContext = uiContext;
        _backend = backend;
        _representationPolicy = representationPolicy;
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
        lock (_sync)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            foreach (ClientState state in _clients.Values)
            {
                CancelAll(state);
            }

            _clients.Clear();
        }

        _foregroundQueue.Dispose();
        _preloadQueue.Dispose();
    }

    // Must be called on the UI thread, serialized per client.
    internal void Load(ImageLoadClient client, string path, Action<ImageLoadResult> completed)
    {
        Log.Debug("Image", $"Open requested: '{path}'.");
        LoadRequest? start;
        lock (_sync)
        {
            ClientState state = GetState(client);
            CancelLoad(state);
            state.CurrentLoad = new LoadRequest(client, path, completed, new CancellationTokenSource());
            start = TakeActive(state);
        }

        if (start is not null)
        {
            _ = ProcessForegroundAsync(start);
        }
    }

    /// <param name="freeBytes">
    /// How many bytes of decoded pixels the caller can still keep. An image that doesn't fit in
    /// what is left is dropped after its header is read, before its pixels are.
    /// </param>
    internal void Preload(
        ImageLoadClient client,
        IEnumerable<string?> paths,
        long freeBytes,
        Action<ImageLoadResult> completed)
    {
        List<PreloadRequest> requests = [];
        lock (_sync)
        {
            ClientState state = GetState(client);
            state.PreloadCancellation.Cancel();
            state.PreloadCancellation = new CancellationTokenSource();
            var batch = new PreloadBatch(freeBytes, state.PreloadCancellation.Token);
            var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string? path in paths)
            {
                if (freeBytes > 0 && !string.IsNullOrWhiteSpace(path) && uniquePaths.Add(path))
                {
                    requests.Add(new PreloadRequest(client, path, batch, completed));
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
        lock (_sync)
        {
            ClientState state = GetState(client);
            CancelLoad(state);
            state.CurrentLoad = null;
        }
    }

    /// <remarks>The caller owns the bitmap and must dispose it.</remarks>
    internal Task<ClipboardBitmap> DecodeClipboardBitmapAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _foregroundQueue.Enqueue(
            (decoder, token) => decoder.DecodeClipboardBitmap(path, token),
            cancellationToken: cancellationToken);
    }

    internal void RemoveClient(ImageLoadClient client)
    {
        lock (_sync)
        {
            if (_clients.Remove(client, out ClientState? state))
            {
                CancelAll(state);
            }
        }
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

    private static void CancelAll(ClientState state)
    {
        CancelLoad(state);
        state.PreloadCancellation.Cancel();
    }

    // The caller must hold _sync. A client decodes one foreground image at a time, so the
    // newest request waits for a superseded decode to stop.
    private static LoadRequest? TakeActive(ClientState state)
    {
        if (state.LoadActive || state.CurrentLoad is not { Started: false } request)
        {
            return null;
        }

        state.LoadActive = true;
        request.Started = true;
        return request;
    }

    private async Task ProcessForegroundAsync(LoadRequest request)
    {
        ImageLoadResult result;
        try
        {
            result = await _foregroundQueue.Enqueue(
                (decoder, token) => DecodeForeground(request.Path, decoder, token),
                cancellationToken: request.Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested)
        {
            FinishLoad(request);
            return;
        }
        catch (Exception exception)
        {
            Log.Error("Image", $"Failed to load '{Path.GetFileName(request.Path)}'.", exception);
            result = new ImageLoadFailed(request.Path, exception);
        }

        PostToUi(() => Deliver(() => IsCurrent(request), request.Completed, result));
        FinishLoad(request);
    }

    private async Task ProcessPreloadAsync(PreloadRequest request)
    {
        ImageLoadResult? result;
        try
        {
            result = await _preloadQueue.Enqueue(
                (decoder, token) => PreloadDecode(request, decoder, token),
                cancellationToken: request.Batch.CancellationToken).ConfigureAwait(false);
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
            PostToUi(() => Deliver(() => _clients.ContainsKey(request.Client), request.Completed, result));
        }
    }

    private ImageLoaded? PreloadDecode(PreloadRequest request, IImageDecoder decoder, CancellationToken cancellationToken)
    {
        string path = request.Path;
        ImageInfo sourceInfo = decoder.GetInfo(path);
        if (SelectRepresentation(path, sourceInfo) != ImageRepresentationKind.Static
            || cancellationToken.IsCancellationRequested
            || !TryClaim(request.Batch, sourceInfo))
        {
            return null;
        }

        // Not cancelled once started, because a foreground load of the same file may be
        // waiting on this decode.
        return new ImageLoaded(
            path,
            new UploadImageRepresentation(DecodeShared(path, decoder, CancellationToken.None)));
    }

    private bool TryClaim(PreloadBatch batch, ImageInfo image)
    {
        long bytes = (long)image.Width * image.Height * 4;
        lock (_sync)
        {
            if (bytes > batch.FreeBytes)
            {
                return false;
            }

            batch.FreeBytes -= bytes;
            return true;
        }
    }

    private ImageLoaded DecodeForeground(
        string path,
        IImageDecoder decoder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ImageInfo sourceInfo = decoder.GetInfo(path);
        cancellationToken.ThrowIfCancellationRequested();
        switch (SelectRepresentation(path, sourceInfo))
        {
            case ImageRepresentationKind.Animated:
                return OpenAnimation(path);
            case ImageRepresentationKind.Tiled:
                Log.Debug("Image", $"Selected tiled representation for '{path}'.");
                return new ImageLoaded(path, new TiledImageRepresentation(_backend.OpenTiledImage(path)));
            default:
                Log.Debug("Image", $"Selected static representation for '{path}'.");
                return new ImageLoaded(
                    path,
                    new UploadImageRepresentation(DecodeShared(path, decoder, cancellationToken)));
        }
    }

    private ImageLoaded OpenAnimation(string path)
    {
        IAnimationSession? animation = _backend.OpenAnimation(path);
        try
        {
            if (!animation.IsAnimated)
            {
                Log.Debug("Image", $"Selected static representation for '{path}'.");
                return new ImageLoaded(path, new DecodedImageRepresentation(animation.FirstFrame.Image));
            }

            var result = new ImageLoaded(path, new AnimatedImageRepresentation(animation));
            Log.Debug("Image", $"Selected animated representation for '{path}'.");
            animation = null;
            return result;
        }
        finally
        {
            animation?.Dispose();
        }
    }

    private ImageRepresentationKind SelectRepresentation(string path, ImageInfo info) =>
        _representationPolicy.Select(info, _backend.SupportsAnimation(path));

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
            DecodedImageUpload shared = pending.Completion.Task
                .WaitAsync(cancellationToken)
                .GetAwaiter()
                .GetResult();
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
            _ = ProcessForegroundAsync(next);
        }
    }

    // The caller must hold _sync.
    private bool IsCurrent(LoadRequest request) =>
        _clients.TryGetValue(request.Client, out ClientState? state)
        && ReferenceEquals(state.CurrentLoad, request);

    // A result nobody wants anymore is released here instead of reaching the caller.
    private void Deliver(Func<bool> isWanted, Action<ImageLoadResult> completed, ImageLoadResult result)
    {
        bool wanted;
        lock (_sync)
        {
            wanted = isWanted();
        }

        if (wanted)
        {
            completed(result);
        }
        else
        {
            (result as ImageLoaded)?.Dispose();
        }
    }

    private void PostToUi(Action action)
    {
        _uiContext.Post(_ => action(), null);
    }

    private sealed class ClientState
    {
        internal LoadRequest? CurrentLoad { get; set; }
        internal bool LoadActive { get; set; }
        internal CancellationTokenSource PreloadCancellation { get; set; } = new();
    }

    private sealed class LoadRequest(
        ImageLoadClient client,
        string path,
        Action<ImageLoadResult> completed,
        CancellationTokenSource cancellation)
    {
        internal ImageLoadClient Client { get; } = client;
        internal string Path { get; } = path;
        internal Action<ImageLoadResult> Completed { get; } = completed;
        internal CancellationTokenSource Cancellation { get; } = cancellation;
        internal bool Started { get; set; }
    }

    private sealed record PreloadRequest(
        ImageLoadClient Client,
        string Path,
        PreloadBatch Batch,
        Action<ImageLoadResult> Completed);

    // The preloads asked for together, which share one cancellation and one amount of space.
    private sealed class PreloadBatch(long freeBytes, CancellationToken cancellationToken)
    {
        internal CancellationToken CancellationToken { get; } = cancellationToken;

        // Guarded by _sync.
        internal long FreeBytes { get; set; } = freeBytes;
    }

    private sealed class InFlightDecode
    {
        private int _consumers;

        internal TaskCompletionSource<DecodedImageUpload> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void AddConsumer() => _consumers++;
        internal int ReleaseConsumer() => --_consumers;
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

    internal void Preload(IEnumerable<string?> paths, long freeBytes, Action<ImageLoadResult> completed)
        => Service.Preload(this, paths, freeBytes, completed);

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
