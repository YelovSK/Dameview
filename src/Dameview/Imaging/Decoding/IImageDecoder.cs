namespace Dameview.Imaging;

internal interface IImageDecoder : IDisposable
{
    public DecodedImage Decode(string path);
}
