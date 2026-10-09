using System.Runtime.InteropServices;

namespace Dameview.Imaging;

// Owns temporary CPU pixels used to upload one static image to a graphics backend.
internal sealed unsafe class DecodedImageUpload : IDisposable
{
    private nint _pixels;

    private DecodedImageUpload(
        int width,
        int height,
        int stride,
        int length,
        ImageOrientation orientation,
        nint pixels)
    {
        Width = width;
        Height = height;
        Stride = stride;
        Length = length;
        Orientation = orientation;
        _pixels = pixels;
    }

    internal int Width { get; }
    internal int Height { get; }
    internal int Stride { get; }
    internal int Length { get; }
    /// <summary>How the pixels, which are in stored order, are meant to be shown.</summary>
    internal ImageOrientation Orientation { get; }
    internal nint Pixels => _pixels != 0
        ? _pixels
        : throw new ObjectDisposedException(nameof(DecodedImageUpload));
    internal Span<byte> Span => new((void*)Pixels, Length);

    internal static DecodedImageUpload Allocate(
        int width,
        int height,
        int stride,
        ImageOrientation orientation = default)
    {
        int length = checked(stride * height);
        void* pixels = NativeMemory.Alloc((nuint)length);
        return new DecodedImageUpload(width, height, stride, length, orientation, (nint)pixels);
    }

    public void Dispose()
    {
        if (_pixels == 0)
        {
            return;
        }

        NativeMemory.Free((void*)_pixels);
        _pixels = 0;
    }
}
