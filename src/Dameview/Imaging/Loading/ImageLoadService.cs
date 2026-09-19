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
                CancelLoad(state);
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
        bool supportsAnimation = _backend.SupportsAnimation(path);
        LoadRequest? start = null;
        lock (_sync)
        {
            ClientState state = GetState(client);
            CancelLoad(state);
            var cancellation = new CancellationTokenSource();
            LoadRequest request = supportsAnimation
                ? new AnimatedLoadRequest(client, path, completed, cancellation)
                : new StaticLoadRequest(client, path, completed, cancellation);
            state.CurrentLoad = request;
            state.PendingLoad = request;
            start = TakeActive(state);
        }

        if (start is not null)
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
        lock (_sync)
        {
            ClientState state = GetState(client);
            CancelLoad(state);
            state.CurrentLoad = null;
            state.PendingLoad = null;
        }
    }

    /// <summary>
    /// Decodes a temporary CPU image and delivers it on the owner thread.
    /// </summary>
    /// <remarks>The callback owns the decoded buffer and must dispose it.</remarks>
    internal void DecodeTemporary(
        string path,
        Action<DecodedImageUpload?, Exception?> completed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(completed);
        _ = ProcessTemporaryDecodeAsync(path, completed, cancellationToken);
    }

    internal void RemoveClient(ImageLoadClient client)
    {
        lock (_sync)
        {
            if (_clients.Remove(client, out ClientState? state))
            {
                CancelLoad(state);
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

    private async Task ProcessTemporaryDecodeAsync(
        string path,
        Action<DecodedImageUpload?, Exception?> completed,
        CancellationToken cancellationToken)
    {
        DecodedImageUpload? image = null;
        Exception? error = null;
        try
        {
            image = await _foregroundQueue.Enqueue(
                (decoder, token) => decoder.DecodeUpload(path, token),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            error = exception;
        }

        DecodedImageUpload? delivery = image;
        image = null;
        try
        {
            PostToUi(() => completed(delivery, error));
        }
        catch
        {
            delivery?.Dispose();
            throw;
        }
    }

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
            Log.Error("Image", $"Failed to load '{Path.GetFileName(request.Path)}'.", exception);
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
            Log.Error("Image", $"Failed to decode '{Path.GetFileName(request.Path)}'.", exception);
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
                result = await _preloadQueue.Enqueue(
                    (decoder, _) => PreloadDecode(request, decoder)).ConfigureAwait(false);
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
            if (_representationPolicy.RequiresTiling(decoder.GetInfo(request.Path))
                || !IsPreloadCurrent(request))
            {
                return null;
            }

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
        if (request is StaticLoadRequest)
        {
            ImageInfo sourceInfo = decoder.GetInfo(request.Path);
            cancellationToken.ThrowIfCancellationRequested();
            if (_representationPolicy.RequiresTiling(sourceInfo))
            {
                Log.Debug("Image", $"Selected tiled representation for '{request.Path}'.");
                IImageTileSource tiledImage = _backend.OpenTiledImage(request.Path);
                return new ImageLoaded(request.Path, new TiledImageRepresentation(tiledImage));
            }

            Log.Debug("Image", $"Selected static representation for '{request.Path}'.");
            return new ImageLoaded(
                request.Path,
                new UploadImageRepresentation(DecodeShared(request.Path, decoder, cancellationToken)));
        }

        IAnimationSession? animation = _backend.OpenAnimation(request.Path);
        try
        {
            if (!animation.IsAnimated)
            {
                Log.Debug("Image", $"Selected static representation for '{request.Path}'.");
                return new ImageLoaded(
                    request.Path,
                    new DecodedImageRepresentation(animation.FirstFrame.Image));
            }

            var result = new ImageLoaded(request.Path, new AnimatedImageRepresentation(animation));
            Log.Debug("Image", $"Selected animated representation for '{request.Path}'.");
            animation = null;
            return result;
        }
        finally
        {
            animation?.Dispose();
        }
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
        lock (_sync)
        {
            accepted = IsCurrentUnsafe(request);
            if (accepted)
            {
                request.IsComplete = true;
            }
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
        CancellationTokenSource cancellation)
        : LoadRequest(client, path, completed, cancellation)
    {
    }

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
