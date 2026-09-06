namespace Dameview.Imaging;

/// <summary>Decodes one still image into CPU-backed pixel data.</summary>
internal interface IImageDecoder : IDisposable
{
    /// <summary>Decodes the image at <paramref name="path"/>.</summary>
    public DecodedImage Decode(string path);
}
