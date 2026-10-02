using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.Shell;
using static Windows.Win32.PInvoke;

namespace Dameview.Win32;

internal static unsafe class Win32Clipboard
{
    // Every format is rendered up front, so no owner window has to answer WM_RENDERFORMAT later,
    // and any thread can set the clipboard.
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
    internal static bool TrySetData(CLIPBOARD_FORMAT format, HGLOBAL memory)
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
}
