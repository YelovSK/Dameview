using System.Diagnostics.CodeAnalysis;
using Dameview.Platform;

namespace Dameview.Imaging;

internal sealed class ImageLoadService : IDisposable
{
    private readonly object _sync = new();
    private readonly UiPost _postToUi;
    private readonly IImageLoadingBackend _backend;
    private readonly ImageRepresentationPolicy _representationPolicy;
    private readonly IThumbnailLoader _thumbnailLoader;
    private readonly Thread[] _foregroundWorkers;
    private readonly Thread _preloadWorker;
    private readonly Queue<ImageLoadClient> _pendingClients = new();
    private readonly Queue<PreloadRequest> _pendingPreloads = new();
    private readonly Dictionary<ImageLoadClient, ClientState> _clients = [];
    private readonly Dictionary<string, InFlightDecode> _inFlightDecodes =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _stopping;

    internal ImageLoadService(
        UiPost postToUi,
        IImageLoadingBackend backend,
        ImageRepresentationPolicy representationPolicy,
        IThumbnailLoader thumbnailLoader)
    {
        _postToUi = postToUi;
        _backend = backend;
        _representationPolicy = representationPolicy;
        _thumbnailLoader = thumbnailLoader;
        _foregroundWorkers =
        [
            CreateWorker("Dameview image loader 1", ForegroundWork),
            CreateWorker("Dameview image loader 2", ForegroundWork),
        ];
        _preloadWorker = CreateWorker("Dameview image preloader", PreloadWork);
        foreach (Thread worker in _foregroundWorkers)
        {
            worker.Start();
        }

        _preloadWorker.Start();
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
            _pendingClients.Clear();
            _pendingPreloads.Clear();
            Monitor.PulseAll(_sync);
        }

        foreach (IDisposable preview in previews)
        {
            preview.Dispose();
        }
    }

    internal void Load(ImageLoadClient client, string path, Action<ImageLoadResult> completed)
    {
        IDisposable? previousPreview;
        lock (_sync)
        {
            ClientState state = GetState(client);
            CancelLoad(state);
            previousPreview = state.PreviewRequest;
            state.PreviewRequest = null;
            var cancellation = new CancellationTokenSource();
            var request = new LoadRequest(client, path, completed, cancellation);
            state.CurrentLoad = request;
            state.PendingLoad = request;
            QueueClient(client, state);
        }

        previousPreview?.Dispose();
    }

    internal void Preload(
        ImageLoadClient client,
        IEnumerable<string?> paths,
        Action<ImageLoadResult> completed)
    {
        lock (_sync)
        {
            ClientState state = GetState(client);
            int generation = ++state.PreloadGeneration;
            var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string? path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path) && uniquePaths.Add(path))
                {
                    _pendingPreloads.Enqueue(new PreloadRequest(
                        client,
                        generation,
                        path,
                        completed));
                }
            }

            Monitor.PulseAll(_sync);
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

    private void QueueClient(ImageLoadClient client, ClientState state)
    {
        if (state.LoadActive || state.LoadQueued)
        {
            return;
        }

        state.LoadQueued = true;
        _pendingClients.Enqueue(client);
        Monitor.PulseAll(_sync);
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
            try
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
                            request,
                            decoder!,
                            request.Cancellation.Token);
                    }
                    catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested)
                    {
                        continue;
                    }
                    catch (Exception exception)
                    {
                        result = new ImageLoadFailed(request.Path, exception);
                    }
                }

                if (IsCurrent(request))
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
            finally
            {
                FinishLoad(request);
            }
        }
    }

    private ImageLoaded DecodeForeground(
        LoadRequest request,
        IImageDecoder decoder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_backend.SupportsAnimation(request.Path))
        {
            ImageInfo sourceInfo = decoder.GetInfo(request.Path);
            RequestPreview(request, sourceInfo);
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
                try
                {
                    _postToUi(() => DeliverPreload(request, delivered));
                }
                catch
                {
                    DisposeResult(delivered);
                    throw;
                }
            }
        }
    }

    private bool RequiresTiledRepresentation(string path, IImageDecoder decoder)
    {
        return _representationPolicy.RequiresTiling(decoder.GetInfo(path));
    }

    private void RequestPreview(LoadRequest request, ImageInfo sourceInfo)
    {
        if (!IsCurrent(request))
        {
            return;
        }

        IDisposable previewRequest = _thumbnailLoader.Request(
            request.Path,
            ThumbnailPriority.Foreground,
            image => Deliver(request, new ImageLoaded(
                request.Path,
                new DecodedImageRepresentation(image, sourceInfo),
                IsPreview: true)));
        bool accepted;
        lock (_sync)
        {
            accepted = IsCurrentUnsafe(request);
            if (accepted)
            {
                _clients[request.Client].PreviewRequest = previewRequest;
            }
        }

        if (!accepted)
        {
            previewRequest.Dispose();
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

    private bool TryTakeRequest([NotNullWhen(true)] out LoadRequest? request)
    {
        lock (_sync)
        {
            while (!_stopping)
            {
                while (_pendingClients.TryDequeue(out ImageLoadClient? client))
                {
                    if (!_clients.TryGetValue(client, out ClientState? state))
                    {
                        continue;
                    }

                    state.LoadQueued = false;
                    if (state.LoadActive || state.PendingLoad is null)
                    {
                        continue;
                    }

                    request = state.PendingLoad;
                    state.PendingLoad = null;
                    state.LoadActive = true;
                    return true;
                }

                Monitor.Wait(_sync);
            }

            request = null;
            return false;
        }
    }

    private void FinishLoad(LoadRequest request)
    {
        lock (_sync)
        {
            if (!_clients.TryGetValue(request.Client, out ClientState? state))
            {
                return;
            }

            state.LoadActive = false;
            if (state.PendingLoad is not null)
            {
                QueueClient(request.Client, state);
            }
        }
    }

    private bool TryTakePreload([NotNullWhen(true)] out PreloadRequest? request)
    {
        lock (_sync)
        {
            while (!_stopping)
            {
                while (_pendingPreloads.TryDequeue(out request))
                {
                    if (_clients.TryGetValue(request.Client, out ClientState? state)
                        && request.Generation == state.PreloadGeneration)
                    {
                        return true;
                    }
                }

                Monitor.Wait(_sync);
            }

            request = null;
            return false;
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

    private sealed class ClientState
    {
        internal LoadRequest? CurrentLoad { get; set; }
        internal LoadRequest? PendingLoad { get; set; }
        internal IDisposable? PreviewRequest { get; set; }
        internal int PreloadGeneration { get; set; }
        internal bool LoadActive { get; set; }
        internal bool LoadQueued { get; set; }
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
        internal bool IsComplete { get; set; }
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
