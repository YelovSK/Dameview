using Dameview.Imaging.Animation;

namespace Dameview.Imaging.Loading;

// Owns the resources needed to present one accepted image. ViewerSession owns the
// representation; presentation consumers borrow it and own their graphics resources.
// Width and Height are the stored size; Orientation says how the stored pixels are shown.
internal abstract class ImageRepresentation(
    int width,
    int height,
    ImageOrientation orientation = default) : IDisposable
{
    private bool _disposed;

    internal int Width { get; } = width;
    internal int Height { get; } = height;
    internal ImageOrientation Orientation { get; } = orientation;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeCore();
    }

    protected virtual void DisposeCore()
    {
    }
}

internal sealed class DecodedImageRepresentation(DecodedImage image)
    : ImageRepresentation(image.Width, image.Height)
{
    internal DecodedImage Image { get; } = image;
}

internal sealed class UploadImageRepresentation(DecodedImageUpload upload)
    : ImageRepresentation(upload.Width, upload.Height, upload.Orientation)
{
    internal DecodedImageUpload Upload { get; } = upload;

    protected override void DisposeCore() => Upload.Dispose();
}

internal sealed class TiledImageRepresentation(IImageTileSource source)
    : ImageRepresentation(source.Width, source.Height, source.Orientation)
{
    internal IImageTileSource Source { get; } = source;

    protected override void DisposeCore() => Source.Dispose();
}

internal sealed class AnimatedImageRepresentation(IAnimationSession animation)
    : ImageRepresentation(animation.FirstFrame.Image.Width, animation.FirstFrame.Image.Height)
{
    internal IAnimationSession Animation { get; } = animation;
    internal AnimatedImagePlayer Player { get; } = new(animation);

    protected override void DisposeCore() => Animation.Dispose();
}
