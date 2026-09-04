using System.Diagnostics.CodeAnalysis;
using Dameview.Platform;

namespace Dameview.Imaging;

internal sealed class ImageLoadCoordinator : IDisposable
{
    private readonly object _sync = new();
    private readonly Action<Action> _postToUi;
    private readonly Func<IImageDecoder> _decoderFactory;
    private readonly Thread _worker;
    private LoadRequest? _pendingRequest;
    private long _latestRequestId;
    private bool _stopping;

    internal ImageLoadCoordinator(
        Action<Action> postToUi,
        Func<IImageDecoder>? decoderFactory = null)
    {
        _postToUi = postToUi;
        _decoderFactory = decoderFactory ?? (static () => new ImageDecoder());
        _worker = new Thread(Work)
        {
            IsBackground = true,
            Name = "Dameview image loader",
        };
        _worker.Start();
    }

    internal void Load(string path, Action<ImageLoadResult> completed)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            _pendingRequest = new LoadRequest(++_latestRequestId, path, completed);
            Monitor.Pulse(_sync);
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
            _pendingRequest = null;
            _latestRequestId++;
            Monitor.Pulse(_sync);
        }
    }

    private void Work()
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
                decoder = _decoderFactory();
            }
            catch (Exception exception)
            {
                initializationError = exception;
            }

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
                        DecodedImage image = decoder!.Decode(request.Path);
                        result = new ImageLoaded(request.Path, image);
                    }
                    catch (Exception exception)
                    {
                        result = new ImageLoadFailed(request.Path, exception);
                    }
                }

                if (!IsLatest(request.Id))
                {
                    continue;
                }

                _postToUi(() => Deliver(request, result));
            }
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

    private void Deliver(LoadRequest request, ImageLoadResult result)
    {
        if (IsLatest(request.Id))
        {
            request.Completed(result);
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
    DecodedImage Image) : ImageLoadResult(Path);

internal sealed record ImageLoadFailed(
    string Path,
    Exception Exception) : ImageLoadResult(Path);
