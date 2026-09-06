namespace Dameview.Imaging;

/// <summary>Loads foreground images and optionally warms neighboring images.</summary>
/// <remarks>An <see cref="ImageLoaded"/> preview may precede the final result. Results are dispatched to the UI thread, and superseded requests are suppressed.</remarks>
internal interface IImageLoader
{
    /// <summary>Loads an image and delivers the latest result to <paramref name="completed"/>.</summary>
    public void Load(string path, Action<ImageLoadResult> completed);
    /// <summary>Speculatively loads the supplied paths without reporting failures to the caller.</summary>
    public void Preload(IEnumerable<string?> paths);
}
