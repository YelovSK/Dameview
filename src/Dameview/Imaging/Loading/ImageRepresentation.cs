namespace Dameview.Imaging;

// Owns the resources needed to present one accepted image. ViewerSession owns the
// representation; UI components borrow it and own only their graphics resources.
internal abstract class ImageRepresentation(int width, int height) : IDisposable
{
    private bool _disposed;

    internal int Width { get; } = width;
    internal int Height { get; } = height;

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

internal sealed class TiledImageRepresentation(IImageTileSource source)
    : ImageRepresentation(source.Width, source.Height)
{
    internal IImageTileSource Source { get; } = source;

    protected override void DisposeCore() => Source.Dispose();
}

internal sealed class AnimatedImageRepresentation(IAnimationSession animation)
    : ImageRepresentation(animation.FirstFrame.Image.Width, animation.FirstFrame.Image.Height)
{
    internal IAnimationSession Animation { get; } = animation;

    protected override void DisposeCore() => Animation.Dispose();
}
