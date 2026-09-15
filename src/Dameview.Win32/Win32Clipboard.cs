using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Ole;
using static Windows.Win32.PInvoke;

namespace Dameview.Win32;

internal static unsafe class Win32Clipboard
{
    private const int BitmapInfoHeaderSize = 40;

    internal static bool TrySetImage(
        nint owner,
        int width,
        int height,
        int stride,
        ReadOnlySpan<byte> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegative(stride);
        ArgumentOutOfRangeException.ThrowIfNotEqual(stride, checked(width * 4));
        ArgumentOutOfRangeException.ThrowIfNotEqual(pixels.Length, checked(stride * height));

        if (!OpenClipboard((HWND)owner))
        {
            return false;
        }

        HGLOBAL dibMemory = HGLOBAL.Null;
        bool dibTransferred = false;
        try
        {
            if (!EmptyClipboard())
            {
                return false;
            }

            dibMemory = AllocateBitmap(width, height, pixels);
            if (dibMemory == HGLOBAL.Null)
            {
                return false;
            }

            if (SetClipboardData(
                    (uint)CLIPBOARD_FORMAT.CF_DIB,
                    (HANDLE)(IntPtr)dibMemory) == HANDLE.Null)
            {
                return false;
            }

            dibTransferred = true;

            return true;
        }
        finally
        {
            if (!dibTransferred && dibMemory != HGLOBAL.Null)
            {
                _ = GlobalFree(dibMemory);
            }

            _ = CloseClipboard();
        }
    }

    private static HGLOBAL AllocateBitmap(
        int width,
        int height,
        ReadOnlySpan<byte> pixels)
    {
        nuint size = checked((nuint)(BitmapInfoHeaderSize + pixels.Length));
        HGLOBAL memory = GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, size);
        if (memory == HGLOBAL.Null)
        {
            return HGLOBAL.Null;
        }

        void* locked = GlobalLock(memory);
        if (locked is null)
        {
            _ = GlobalFree(memory);
            return HGLOBAL.Null;
        }

        bool completed = false;
        try
        {
            Span<byte> destination = new(locked, checked((int)size));
            WriteBitmapInfoHeader(destination, width, height, pixels.Length);
            CopyPixelsToDib(
                pixels,
                destination[BitmapInfoHeaderSize..],
                width,
                height,
                checked(width * 4));
            completed = true;
            return memory;
        }
        finally
        {
            _ = GlobalUnlock(memory);
            if (!completed)
            {
                _ = GlobalFree(memory);
            }
        }
    }

    private static void CopyPixelsToDib(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        int width,
        int height,
        int stride)
    {
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> sourceRow = source.Slice(y * stride, stride);
            Span<byte> destinationRow = destination.Slice((height - 1 - y) * stride, stride);
            for (int x = 0; x < width; x++)
            {
                int offset = x * 4;
                byte alpha = sourceRow[offset + 3];
                destinationRow[offset] = Unpremultiply(sourceRow[offset], alpha);
                destinationRow[offset + 1] = Unpremultiply(sourceRow[offset + 1], alpha);
                destinationRow[offset + 2] = Unpremultiply(sourceRow[offset + 2], alpha);
                destinationRow[offset + 3] = alpha;
            }
        }
    }

    private static byte Unpremultiply(byte channel, byte alpha)
    {
        return alpha == 0
            ? (byte)0
            : (byte)Math.Min(255, (channel * 255 + alpha / 2) / alpha);
    }

    private static void WriteBitmapInfoHeader(
        Span<byte> destination,
        int width,
        int height,
        int imageSize)
    {
        int headerSize = BitmapInfoHeaderSize;
        MemoryMarshal.Write(destination[0..4], in headerSize);
        MemoryMarshal.Write(destination[4..8], in width);

        MemoryMarshal.Write(destination[8..12], in height);

        short planes = 1;
        short bitsPerPixel = 32;
        MemoryMarshal.Write(destination[12..14], in planes);
        MemoryMarshal.Write(destination[14..16], in bitsPerPixel);

        uint compression = 0;
        MemoryMarshal.Write(destination[16..20], in compression);
        MemoryMarshal.Write(destination[20..24], in imageSize);
    }

}
