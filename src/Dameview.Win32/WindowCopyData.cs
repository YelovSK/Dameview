using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.PInvoke;

namespace Dameview.Win32;

internal static unsafe class WindowCopyData
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Data
    {
        internal nuint DataId;
        internal uint ByteCount;
        internal nint DataPointer;
    }

    internal static bool TrySend(string windowClassName, nuint dataId, string payload)
    {
        fixed (char* className = windowClassName)
        fixed (char* text = payload)
        {
            HWND window = FindWindow(className, default);
            if (window.IsNull)
            {
                return false;
            }

            var data = new Data
            {
                DataId = dataId,
                ByteCount = checked((uint)(payload.Length * sizeof(char))),
                DataPointer = payload.Length == 0 ? 0 : (nint)text,
            };
            _ = GetWindowThreadProcessId(window, out uint processId) != 0
                && AllowSetForegroundWindow(processId);
            return SendMessage(
                window,
                WM_COPYDATA,
                default,
                (LPARAM)(nint)(&data)) != 0;
        }
    }
}
