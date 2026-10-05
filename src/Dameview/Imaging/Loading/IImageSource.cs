namespace Dameview.Imaging.Loading;

/// <summary>
/// Knows how to load one <see cref="ImageVariant"/>. The pipeline decides when, and runs it on
/// the source's own workers, so a slow source never holds up a fast one.
/// </summary>
internal interface IImageSource
{
    public string WorkerName { get; }

    /// <summary>Workers for wanted images. Preloads get one more of their own.</summary>
    public int WorkerCount { get; }

    /// <summary>Called once on each worker, which disposes it if it is disposable.</summary>
    public object? CreateWorkerState();

    /// <summary>Runs on one of the source's workers.</summary>
    /// <returns>
    /// The image, or <see langword="null"/> when <see cref="ImageLoadContext.ShouldLoad"/>
    /// said to skip it. Static images are uploaded and cached; any other kind is handed
    /// straight to whoever asked for it.
    /// </returns>
    public ImageRepresentation? Load(object? workerState, string path, ImageLoadContext context);
}

internal abstract class ImageLoadContext
{
    internal abstract CancellationToken CancellationToken { get; }

    /// <summary>
    /// Sources that learn an image's size before decoding it ask this, so a preload that
    /// would not be kept is dropped before its pixels are decoded.
    /// </summary>
    /// <param name="cachedBytes">
    /// What the image will take in the cache, or <see langword="null"/> when it is not a static
    /// image and won't be cached at all.
    /// </param>
    internal abstract bool ShouldLoad(long? cachedBytes);
}
