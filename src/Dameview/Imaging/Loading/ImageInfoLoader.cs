using Dameview.Imaging.Decoding;
using Dameview.Win32;

namespace Dameview.Imaging.Loading;

internal interface IImageInfoLoader
{
    public Task<ImageInfo> LoadAsync(string path, CancellationToken cancellationToken);
}

// Resolves source image dimensions on a dedicated worker so foreground previews and the tiling
// decision never wait behind an in-flight pixel decode.
internal sealed class ImageInfoLoader : IImageInfoLoader, IDisposable
{
    private readonly Lock _sync = new();
    private readonly ComWorkerQueue<IImageDecoder> _queue;
    private readonly Dictionary<string, Task<ImageInfo>> _inFlight =
        new(StringComparer.OrdinalIgnoreCase);
    internal ImageInfoLoader(Func<IImageDecoder> decoderFactory)
    {
        _queue = new ComWorkerQueue<IImageDecoder>("Dameview image info", 1, decoderFactory);
    }

    public Task<ImageInfo> LoadAsync(string path, CancellationToken cancellationToken)
    {
        Task<ImageInfo> shared;

        lock (_sync)
        {
            if (!_inFlight.TryGetValue(path, out shared!))
            {
                shared = _queue.Enqueue(
                    (decoder, _) =>
                        decoder.GetInfo(path),
                    cancellationToken: CancellationToken.None);
                _inFlight.Add(path, shared);
                _ = RemoveWhenCompleteAsync(path, shared);
            }
        }

        // Cancellation belongs to this caller, not to the shared metadata operation.
        return shared.WaitAsync(cancellationToken);
    }

    private async Task RemoveWhenCompleteAsync(string path, Task<ImageInfo> request)
    {
        try
        {
            await request.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The caller observes the original exception through the shared task.
        }

        lock (_sync)
        {
            if (_inFlight.TryGetValue(path, out Task<ImageInfo>? current) &&
                ReferenceEquals(current, request))
            {
                _inFlight.Remove(path);
            }
        }
    }

    public void Dispose() => _queue.Dispose();
}
