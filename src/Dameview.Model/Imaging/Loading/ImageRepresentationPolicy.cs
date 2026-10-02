namespace Dameview.Imaging.Loading;

internal readonly record struct ImageInfo(int Width, int Height, int FrameCount);

internal enum ImageRepresentationKind
{
    Static,
    Animated,
    Tiled,
}

internal readonly record struct ImageRepresentationPolicy
{
    internal const long DefaultMaximumDecodedBytes = 128L * 1024L * 1024L;
    private const int BytesPerPixel = 4;

    internal ImageRepresentationPolicy(
        int maximumBitmapDimension,
        long maximumDecodedBytes = DefaultMaximumDecodedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBitmapDimension);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDecodedBytes, BytesPerPixel);
        MaximumBitmapDimension = maximumBitmapDimension;
        MaximumDecodedBytes = maximumDecodedBytes;
    }

    internal int MaximumBitmapDimension { get; }
    internal long MaximumDecodedBytes { get; }

    /// <param name="canAnimate">Whether an animation decoder handles the image's format.</param>
    internal ImageRepresentationKind Select(ImageInfo image, bool canAnimate)
    {
        if (image.FrameCount > 1 && canAnimate)
        {
            return ImageRepresentationKind.Animated;
        }

        if (RequiresTiling(image))
        {
            return ImageRepresentationKind.Tiled;
        }

        return ImageRepresentationKind.Static;
    }

    private bool RequiresTiling(ImageInfo image)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(image.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(image.Height);

        return image.Width > MaximumBitmapDimension
            || image.Height > MaximumBitmapDimension
            || (long)image.Width * image.Height > MaximumDecodedBytes / BytesPerPixel;
    }
}
