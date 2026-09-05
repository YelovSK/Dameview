namespace Dameview.Imaging;

internal sealed class WindowsImageLoadingBackend : IImageLoadingBackend
{
    private static readonly IAnimatedImageDecoder[] AnimatedDecoders =
    [
        new WicGifAnimationDecoder(),
    ];

    public IImageDecoder CreateDecoder() => new ImageDecoder();

    public DecodedImage? LoadThumbnail(string path) => WindowsThumbnail.Load(path);

    public bool SupportsAnimation(string path) => FindAnimationDecoder(path) is not null;

    public IAnimationSession OpenAnimation(string path)
    {
        IAnimatedImageDecoder decoder = FindAnimationDecoder(path)
            ?? throw new NotSupportedException("The image format does not support animation.");
        return decoder.Open(path);
    }

    private static IAnimatedImageDecoder? FindAnimationDecoder(string path)
    {
        foreach (IAnimatedImageDecoder decoder in AnimatedDecoders)
        {
            if (decoder.CanDecode(path))
            {
                return decoder;
            }
        }

        return null;
    }
}
