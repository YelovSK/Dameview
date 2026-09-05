namespace Dameview.Imaging;

internal interface IImageLoadingBackend
{
    public IImageDecoder CreateDecoder();

    public DecodedImage? LoadThumbnail(string path);

    public bool SupportsAnimation(string path);

    // The caller owns the returned session.
    public IAnimationSession OpenAnimation(string path);
}
