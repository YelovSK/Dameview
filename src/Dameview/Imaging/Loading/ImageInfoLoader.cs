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
    private readonly BackgroundQueue<IImageDecoder> _queue;

    internal ImageInfoLoader(Func<IImageDecoder> decoderFactory)
    {
        _queue = new BackgroundQueue<IImageDecoder>("Dameview image info", 1, decoderFactory);
    }

    public Task<ImageInfo> LoadAsync(string path, CancellationToken cancellationToken) =>
        _queue.Enqueue(
            (decoder, _) => decoder.GetInfo(path),
            cancellationToken: cancellationToken);

    public void Dispose() => _queue.Dispose();
}
