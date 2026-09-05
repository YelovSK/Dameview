namespace Dameview.Imaging;

internal interface IAnimatedImageDecoder
{
    public bool CanDecode(string path);

    public IAnimationSession Open(string path);
}
