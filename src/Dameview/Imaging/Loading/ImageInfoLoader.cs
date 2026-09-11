using System.Diagnostics.CodeAnalysis;
using Dameview.Platform;

namespace Dameview.Imaging;

internal interface IImageInfoLoader
{
    public Task<ImageInfo> LoadAsync(string path, CancellationToken cancellationToken);
}

// Resolves source image dimensions on a dedicated worker so foreground previews and the tiling
// decision never wait behind an in-flight pixel decode.
internal sealed class ImageInfoLoader : IImageInfoLoader, IDisposable
{
    private readonly object _sync = new();
    private readonly Func<IImageDecoder> _decoderFactory;
    private readonly Queue<InfoRequest> _pending = new();
    private readonly Thread _worker;
    private bool _stopping;
    private Exception? _failure;

    internal ImageInfoLoader(Func<IImageDecoder> decoderFactory)
    {
        _decoderFactory = decoderFactory;
        _worker = new Thread(Work)
        {
            IsBackground = true,
            Name = "Dameview image info",
        };
        _worker.Start();
    }

    public Task<ImageInfo> LoadAsync(string path, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_failure is not null)
            {
                return Task.FromException<ImageInfo>(_failure);
            }

            if (_stopping)
            {
                return Task.FromException<ImageInfo>(new ObjectDisposedException(nameof(ImageInfoLoader)));
            }

            var request = new InfoRequest(path, cancellationToken);
            _pending.Enqueue(request);
            Monitor.Pulse(_sync);
            return request.Completion.Task;
        }
    }

    public void Dispose()
    {
        List<InfoRequest> pending;
        lock (_sync)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            pending = [.. _pending];
            _pending.Clear();
            Monitor.PulseAll(_sync);
        }

        foreach (InfoRequest request in pending)
        {
            request.Completion.TrySetCanceled(request.CancellationToken);
        }
    }

    private void Work()
    {
        bool comInitialized = false;
        IImageDecoder? decoder = null;
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
                Fail(exception);
                return;
            }

            while (TryTake(out InfoRequest? request))
            {
                if (request.CancellationToken.IsCancellationRequested)
                {
                    request.Completion.TrySetCanceled(request.CancellationToken);
                    continue;
                }

                try
                {
                    ImageInfo info = decoder!.GetInfo(request.Path);
                    request.Completion.TrySetResult(info);
                }
                catch (Exception) when (request.CancellationToken.IsCancellationRequested)
                {
                    request.Completion.TrySetCanceled(request.CancellationToken);
                }
                catch (Exception exception)
                {
                    request.Completion.TrySetException(exception);
                }
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

    private bool TryTake([NotNullWhen(true)] out InfoRequest? request)
    {
        lock (_sync)
        {
            while (!_stopping)
            {
                while (_pending.TryDequeue(out request))
                {
                    if (!request.CancellationToken.IsCancellationRequested)
                    {
                        return true;
                    }

                    request.Completion.TrySetCanceled(request.CancellationToken);
                }

                Monitor.Wait(_sync);
            }

            request = null;
            return false;
        }
    }

    private void Fail(Exception exception)
    {
        List<InfoRequest> pending;
        lock (_sync)
        {
            _failure = exception;
            _stopping = true;
            pending = [.. _pending];
            _pending.Clear();
            Monitor.PulseAll(_sync);
        }

        foreach (InfoRequest request in pending)
        {
            request.Completion.TrySetException(exception);
        }
    }

    private sealed class InfoRequest(string path, CancellationToken cancellationToken)
    {
        internal string Path { get; } = path;
        internal CancellationToken CancellationToken { get; } = cancellationToken;
        internal TaskCompletionSource<ImageInfo> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
