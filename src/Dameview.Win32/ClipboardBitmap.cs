using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Ole;
using static Windows.Win32.PInvoke;

namespace Dameview.Win32;

/// <summary>
/// A 32-bit bitmap built directly in clipboard memory, so it is handed over without another copy.
/// </summary>
internal sealed unsafe class ClipboardBitmap : IDisposable
{
    private const int BytesPerPixel = 4;

    private readonly int _height;
    private readonly int _stride;
    private readonly byte* _pixels;
    private HGLOBAL _memory;

    private ClipboardBitmap(int height, int stride, HGLOBAL memory, byte* pixels)
    {
        _height = height;
        _stride = stride;
        _memory = memory;
        _pixels = pixels;
    }

    internal static ClipboardBitmap Allocate(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int stride = checked(width * BytesPerPixel);
        int imageSize = checked(stride * height);
        HGLOBAL memory = GlobalAlloc(
            GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE,
            checked((nuint)(sizeof(BITMAPINFOHEADER) + imageSize)));
        if (memory == HGLOBAL.Null)
        {
            throw new InsufficientMemoryException("Could not allocate clipboard memory for the image.");
        }

        byte* locked = (byte*)GlobalLock(memory);
        if (locked is null)
        {
            _ = GlobalFree(memory);
            throw new InsufficientMemoryException("Could not lock clipboard memory for the image.");
        }

        *(BITMAPINFOHEADER*)locked = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = width,
            biHeight = height,
            biPlanes = 1,
            biBitCount = 32,
            biSizeImage = (uint)imageSize,
        };
        return new ClipboardBitmap(height, stride, memory, locked + sizeof(BITMAPINFOHEADER));
    }

    /// <summary>Where row <paramref name="y"/>, counted from the top, goes as straight-alpha BGRA.</summary>
    /// <remarks>
    /// A positive height stores the bottom row first. Some apps misread top-down bitmaps on the
    /// clipboard, so the rows are placed in reverse rather than writing a negative height.
    /// </remarks>
    internal Span<byte> GetRow(int y)
    {
        ObjectDisposedException.ThrowIf(_memory == HGLOBAL.Null, this);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, _height);
        return new(_pixels + ((nint)(_height - 1 - y) * _stride), _stride);
    }

    /// <summary>Puts the bitmap on the clipboard. It can't be used afterwards, whatever the result.</summary>
    internal bool TrySet() => Win32Clipboard.TrySetData(CLIPBOARD_FORMAT.CF_DIB, Release());

    public void Dispose()
    {
        HGLOBAL memory = Release();
        if (memory != HGLOBAL.Null)
        {
            _ = GlobalFree(memory);
        }
    }

    private HGLOBAL Release()
    {
        HGLOBAL memory = _memory;
        if (memory != HGLOBAL.Null)
        {
            _ = GlobalUnlock(memory);
            _memory = HGLOBAL.Null;
        }

        return memory;
    }
}
