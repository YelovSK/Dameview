namespace Dameview.Imaging.Loading;

internal enum ImageVariant
{
    Full,
    Thumbnail,
}

/// <summary>Higher priorities are loaded first.</summary>
internal enum ImagePriority
{
    /// <summary>
    /// Wanted only in case it is shown soon. It is skipped when it would not fit in the cache
    /// next to what is there, and never evicts anything.
    /// </summary>
    Preload,
    Gallery,
    Display,
}

internal readonly record struct ImageKey(string Path, ImageVariant Variant)
{
    public bool Equals(ImageKey other) =>
        Variant == other.Variant && string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        HashCode.Combine(Variant, StringComparer.OrdinalIgnoreCase.GetHashCode(Path));
}

/// <summary>
/// The one way to get an image. It loads each image once however many ask for it, in order of
/// the most urgent request, stops loading what nobody wants anymore, and caches the result.
/// </summary>
/// <remarks>Window thread only.</remarks>
internal interface IImagePipeline
{
    /// <summary>
    /// Delivers the image to <paramref name="completed"/>, which takes ownership of the result.
    /// A cached image is delivered before this returns. Disposing the returned handle means
    /// the image is no longer wanted, and nothing is delivered after that.
    /// </summary>
    /// <remarks>A preload may never be delivered, when there is no room to keep it.</remarks>
    public IDisposable Request(ImageKey key, ImagePriority priority, Action<ImageLoadResult> completed);
}

internal static class ImagePipelineExtensions
{
    /// <summary>Requests a thumbnail, which is best-effort: failures are not delivered.</summary>
    internal static IDisposable RequestThumbnail(
        this IImagePipeline pipeline,
        string path,
        ImagePriority priority,
        Action<CachedBitmapLease> completed) =>
        pipeline.Request(
            new ImageKey(path, ImageVariant.Thumbnail),
            priority,
            result =>
            {
                if (result is ImageLoaded { Representation: CachedBitmapLease lease })
                {
                    completed(lease);
                }
            });
}
