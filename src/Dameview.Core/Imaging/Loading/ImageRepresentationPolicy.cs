namespace Dameview.Imaging.Loading;

internal readonly record struct ImageInfo(int Width, int Height, int FrameCount);

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

    internal bool RequiresTiling(ImageInfo image)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(image.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(image.Height);

        return image.Width > MaximumBitmapDimension
            || image.Height > MaximumBitmapDimension
            || (long)image.Width * image.Height > MaximumDecodedBytes / BytesPerPixel;
    }
}
