using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.System.Com;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.PInvoke;

namespace Dameview.Platform;

internal static unsafe partial class NativeMethods
{
    internal const uint MessageRenderFrame = WM_APP;
    internal const uint MessageDispatch = WM_APP + 1;

    internal static void EnablePerMonitorDpiAwareness()
    {
        // A failure only means awareness was already selected by a manifest or host.
        _ = SetProcessDpiAwarenessContext((DPI_AWARENESS_CONTEXT)new nint(-4));
    }

    internal static void InitializeComApartment(ComApartment apartment)
    {
        HRESULT result = CoInitializeEx((COINIT)apartment);
        if (result.Failed)
        {
            Marshal.ThrowExceptionForHR((int)result);
        }
    }

    internal static void UninitializeComApartment()
    {
        CoUninitialize();
    }

    internal static Win32Exception CreateLastErrorException(string operation)
    {
        return new Win32Exception(Marshal.GetLastPInvokeError(), operation);
    }

    internal static unsafe string GetDroppedFilePath(nint dropHandle)
    {
        uint length = DragQueryFile((HDROP)dropHandle, 0, default, 0);
        if (length == 0)
        {
            return string.Empty;
        }

        char[] buffer = new char[length + 1];
        fixed (char* bufferPointer = buffer)
        {
            uint copied = DragQueryFile((HDROP)dropHandle, 0, bufferPointer, (uint)buffer.Length);
            return new string(bufferPointer, 0, checked((int)copied));
        }
    }

    internal static unsafe WAIT_EVENT WaitForMessageOrHandle(nint handle, bool includeHandle)
    {
        var h = (HANDLE)handle;
        HANDLE* handles = includeHandle ? &h : null;
        return MsgWaitForMultipleObjectsEx(
            includeHandle ? 1u : 0u,
            handles,
            INFINITE,
            QUEUE_STATUS_FLAGS.QS_ALLINPUT,
            MSG_WAIT_FOR_MULTIPLE_OBJECTS_EX_FLAGS.MWMO_INPUTAVAILABLE);
    }

    internal static void SetDwmWindowAttribute(HWND window, DWMWINDOWATTRIBUTE attribute, int value)
    {
        // Unsupported attributes return an HRESULT on older Windows versions.
        _ = DwmSetWindowAttribute(window, attribute, &value, sizeof(int));
    }
}

internal enum ComApartment : uint
{
    MultiThreaded = 0x0,
    ApartmentThreaded = 0x2,
}
