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
    internal ImageRepresentationPolicy(int maximumBitmapDimension)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBitmapDimension);
        MaximumBitmapDimension = maximumBitmapDimension;
    }

    internal int MaximumBitmapDimension { get; }

    /// <param name="canAnimate">Whether an animation decoder handles the image's format.</param>
    /// <param name="cacheCapacityBytes">How much the bitmap cache may hold.</param>
    internal ImageRepresentationKind Select(ImageInfo image, bool canAnimate, long cacheCapacityBytes)
    {
        if (image.FrameCount > 1 && canAnimate)
        {
            return ImageRepresentationKind.Animated;
        }

        if (RequiresTiling(image, cacheCapacityBytes))
        {
            return ImageRepresentationKind.Tiled;
        }

        return ImageRepresentationKind.Static;
    }

    // An image that takes more than half the cache would leave no room to preload a neighbour
    // of its size, and would evict everything else for nothing. Tiled, it stays out of the cache.
    private bool RequiresTiling(ImageInfo image, long cacheCapacityBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(image.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(image.Height);

        return image.Width > MaximumBitmapDimension
            || image.Height > MaximumBitmapDimension
            || DecodedImage.GetByteCount(image.Width, image.Height) > cacheCapacityBytes / 2;
    }
}
