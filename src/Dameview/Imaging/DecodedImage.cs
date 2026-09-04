namespace Dameview.Imaging;

internal sealed class DecodedImage
{
    internal DecodedImage(int width, int height, int stride, byte[] pixels)
    {
        Width = width;
        Height = height;
        Stride = stride;
        Pixels = pixels;
    }

    internal int Width { get; }
    internal int Height { get; }
    internal int Stride { get; }
    internal byte[] Pixels { get; }
}

