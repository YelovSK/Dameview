namespace Dameview.Imaging;

/// <summary>Pixels in 32-bit premultiplied BGRA, the one format the app decodes to.</summary>
internal sealed class DecodedImage
{
    internal const int BytesPerPixel = 4;

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

    /// <summary>The stride of tightly packed rows.</summary>
    internal static int GetStride(int width) => checked(width * BytesPerPixel);

    internal static long GetByteCount(int width, int height) => checked((long)width * height * BytesPerPixel);
}

