using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.Shell;
using static Windows.Win32.PInvoke;

namespace Dameview.Win32;

internal static unsafe class Win32Clipboard
{
    private const int BitmapInfoHeaderSize = 40;

    // Every format is rendered up front, so no owner window has to answer WM_RENDERFORMAT later,
    // and any thread can set the clipboard.
    internal static bool TrySetImage(
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

        return TrySetData(CLIPBOARD_FORMAT.CF_DIB, AllocateBitmap(width, height, pixels));
    }

    internal static bool TrySetText(string text) =>
        TrySetData(CLIPBOARD_FORMAT.CF_UNICODETEXT, AllocateText(text));

    // Pastes as the file itself, the same as copying it in Explorer.
    internal static bool TrySetFile(string path) =>
        TrySetData(CLIPBOARD_FORMAT.CF_HDROP, AllocateFileList(path));

    internal static string? TryGetText()
    {
        if (!OpenClipboard(HWND.Null))
        {
            return null;
        }

        try
        {
            HANDLE data = GetClipboardData((uint)CLIPBOARD_FORMAT.CF_UNICODETEXT);
            if (data == HANDLE.Null)
            {
                return null;
            }

            var memory = (HGLOBAL)(IntPtr)data;
            char* locked = (char*)GlobalLock(memory);
            if (locked is null)
            {
                return null;
            }

            try
            {
                return new string(locked);
            }
            finally
            {
                _ = GlobalUnlock(memory);
            }
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    // Takes ownership of the memory: the clipboard keeps it on success, otherwise it is freed.
    private static bool TrySetData(CLIPBOARD_FORMAT format, HGLOBAL memory)
    {
        if (memory == HGLOBAL.Null)
        {
            return false;
        }

        bool transferred = false;
        try
        {
            if (!OpenClipboard(HWND.Null))
            {
                return false;
            }

            try
            {
                transferred = EmptyClipboard()
                    && SetClipboardData((uint)format, (HANDLE)(IntPtr)memory) != HANDLE.Null;
                return transferred;
            }
            finally
            {
                _ = CloseClipboard();
            }
        }
        finally
        {
            if (!transferred)
            {
                _ = GlobalFree(memory);
            }
        }
    }

    private static HGLOBAL AllocateText(string text)
    {
        nuint size = checked((nuint)((text.Length + 1) * sizeof(char)));
        HGLOBAL memory = GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, size);
        if (memory == HGLOBAL.Null)
        {
            return HGLOBAL.Null;
        }

        char* locked = (char*)GlobalLock(memory);
        if (locked is null)
        {
            _ = GlobalFree(memory);
            return HGLOBAL.Null;
        }

        text.CopyTo(new Span<char>(locked, text.Length));
        locked[text.Length] = '\0';
        _ = GlobalUnlock(memory);
        return memory;
    }

    private static HGLOBAL AllocateFileList(string path)
    {
        int headerSize = sizeof(DROPFILES);

        // Zeroed memory terminates both the path and the list.
        nuint size = checked((nuint)(headerSize + (path.Length + 2) * sizeof(char)));
        HGLOBAL memory = GlobalAlloc(
            GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE | GLOBAL_ALLOC_FLAGS.GMEM_ZEROINIT,
            size);
        if (memory == HGLOBAL.Null)
        {
            return HGLOBAL.Null;
        }

        byte* locked = (byte*)GlobalLock(memory);
        if (locked is null)
        {
            _ = GlobalFree(memory);
            return HGLOBAL.Null;
        }

        *(DROPFILES*)locked = new DROPFILES { pFiles = (uint)headerSize, fWide = true };
        path.CopyTo(new Span<char>(locked + headerSize, path.Length));
        _ = GlobalUnlock(memory);
        return memory;
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
