using Dameview.Diagnostics;

namespace Dameview.Imaging.Loading;

/// <summary>
/// A tab's image loading: shows the thumbnail until the full image arrives, and keeps the
/// images next to it preloaded.
/// </summary>
internal sealed class ViewerImageLoader(IImagePipeline pipeline) : IImageLoader
{
    private IDisposable? _full;
    private IDisposable? _preview;
    private List<PreloadedImage> _preloads = [];
    private bool _disposed;

    public void Load(string path, Action<ImageLoadResult> completed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IDisposable? previous = _full;
        _full = null;
        DropPreview();
        bool loaded = false;
        IDisposable full = pipeline.Request(
            new ImageKey(path, ImageVariant.Full),
            ImagePriority.Display,
            result =>
            {
                loaded = true;
                _full = null;
                DropPreview();
                if (result is ImageLoadFailed failed)
                {
                    Log.Error("Image", $"Failed to load '{Path.GetFileName(path)}'.", failed.Exception);
                }

                completed(result);
            });
        previous?.Dispose();
        if (loaded)
        {
            return;
        }

        _full = full;
        _preview = pipeline.RequestThumbnail(
            path,
            ImagePriority.Display,
            lease =>
            {
                _preview = null;
                completed(new ImageLoaded(path, lease, IsPreview: true));
            });
    }

    public void Preload(IEnumerable<string?> paths)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        List<PreloadedImage> previous = _preloads;
        _preloads = [];
        foreach (string? path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                var preload = new PreloadedImage();
                preload.Request = pipeline.Request(
                    new ImageKey(path, ImageVariant.Full),
                    ImagePriority.Preload,
                    result => preload.Image = result as ImageLoaded);
                _preloads.Add(preload);
            }
        }

        // Dropped only now, so an image that is still wanted keeps loading instead of starting over.
        previous.ForEach(preload => preload.Dispose());
    }

    private void DropPreview()
    {
        _preview?.Dispose();
        _preview = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _full?.Dispose();
        DropPreview();
        _preloads.ForEach(preload => preload.Dispose());
    }

    // Holds on to the loaded image, so the cache keeps it while it is still nearby.
    private sealed class PreloadedImage : IDisposable
    {
        internal IDisposable? Request { get; set; }
        internal ImageLoaded? Image { get; set; }

        public void Dispose()
        {
            Request?.Dispose();
            Image?.Dispose();
        }
    }
}
