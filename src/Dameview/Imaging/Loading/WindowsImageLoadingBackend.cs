using Dameview.Imaging.Animation;
using Dameview.Imaging.Decoding;

namespace Dameview.Imaging.Loading;

internal sealed class WindowsImageLoadingBackend : IImageLoadingBackend
{
    private static readonly IAnimatedImageDecoder[] AnimatedDecoders =
    [
        new WicGifAnimationDecoder(),
        new WicWebpAnimationDecoder(),
        new WicJxlAnimationDecoder(),
    ];
    public IImageDecoder CreateDecoder() => new ImageDecoder();

    public IImageTileSource OpenTiledImage(string path) => WicImageTileSource.Open(path);

    public DecodedImageUpload? LoadThumbnail(string path) => WindowsThumbnail.Load(path);

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
