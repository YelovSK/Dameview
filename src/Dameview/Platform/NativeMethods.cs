using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Dameview.Platform;

internal static partial class NativeMethods
{
    internal const uint ClassHorizontalRedraw = 0x0002;
    internal const uint ClassVerticalRedraw = 0x0001;
    internal const uint ClassDoubleClicks = 0x0008;
    internal const uint WindowStyleOverlappedWindow = 0x00CF0000;
    internal const int UseDefault = unchecked((int)0x80000000);
    internal const int ShowNormal = 1;
    internal const int ShowMaximized = 3;
    internal const uint RemoveMessage = 0x0001;
    internal const uint WaitObject0 = 0x00000000;
    internal const uint WaitFailed = 0xFFFFFFFF;

    internal const uint DwmUseImmersiveDarkMode = 20;
    internal const uint DwmCaptionColor = 35;
    internal const uint DwmTextColor = 36;

    private const uint Infinite = 0xFFFFFFFF;
    private const uint QueueAllInput = 0x04FF;
    private const uint MessageWaitInputAvailable = 0x0004;

    internal const uint MessageDestroy = 0x0002;
    internal const uint MessageQuit = 0x0012;
    internal const uint MessageSize = 0x0005;
    internal const uint MessagePaint = 0x000F;
    internal const uint MessageEraseBackground = 0x0014;
    internal const uint MessageKeyDown = 0x0100;
    internal const uint MessageMouseMove = 0x0200;
    internal const uint MessageSetCursor = 0x0020;
    internal const uint MessageLeftButtonDown = 0x0201;
    internal const uint MessageLeftButtonUp = 0x0202;
    internal const uint MessageLeftButtonDoubleClick = 0x0203;
    internal const uint MessageMouseWheel = 0x020A;
    internal const uint MessageCaptureChanged = 0x0215;
    internal const uint MessageDropFiles = 0x0233;
    internal const uint MessageDpiChanged = 0x02E0;
    internal const uint MessageNonClientCreate = 0x0081;
    internal const uint MessageRenderFrame = 0x8000;
    internal const uint MessageDispatch = 0x8001;

    [LibraryImport("user32")]
    internal static partial short GetKeyState(int virtualKey);

    internal static void EnablePerMonitorDpiAwareness()
    {
        // A failure only means awareness was already selected by a manifest or host.
        _ = SetProcessDpiAwarenessContext(new nint(-4));
    }

    internal static void InitializeComApartment(ComApartment apartment)
    {
        int result = CoInitializeEx(0, (uint)apartment);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
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
        uint length = DragQueryFile(dropHandle, 0, null, 0);
        if (length == 0)
        {
            return string.Empty;
        }

        char[] buffer = new char[length + 1];
        fixed (char* bufferPointer = buffer)
        {
            uint copied = DragQueryFile(dropHandle, 0, bufferPointer, (uint)buffer.Length);
            return new string(bufferPointer, 0, checked((int)copied));
        }
    }

    internal static unsafe uint WaitForMessageOrHandle(nint handle, bool includeHandle)
    {
        nint* handles = includeHandle ? &handle : null;
        return MsgWaitForMultipleObjectsEx(
            includeHandle ? 1u : 0u,
            handles,
            Infinite,
            QueueAllInput,
            MessageWaitInputAvailable);
    }

    internal static void SetDwmWindowAttribute(nint window, uint attribute, int value)
    {
        // Unsupported attributes return an HRESULT on older Windows versions.
        _ = DwmSetWindowAttribute(window, attribute, in value, sizeof(int));
    }

    [LibraryImport("ole32")]
    private static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32")]
    private static partial void CoUninitialize();

    [LibraryImport("dwmapi", EntryPoint = "DwmSetWindowAttribute")]
    private static partial int DwmSetWindowAttribute(
        nint window,
        uint attribute,
        in int value,
        int valueSize);

    [LibraryImport("user32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDpiAwarenessContext(nint value);

    [LibraryImport("kernel32", EntryPoint = "GetModuleHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle(string? moduleName);

    [LibraryImport("user32", EntryPoint = "RegisterClassExW", SetLastError = true)]
    internal static partial ushort RegisterClass(ref WindowClass windowClass);

    [LibraryImport("user32", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindow(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [LibraryImport("user32", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowText(nint window, string title);

    [LibraryImport("user32", EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32", EntryPoint = "DestroyWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint window);

    [LibraryImport("user32", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32", EntryPoint = "GetWindowPlacement")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowPlacement(nint window, ref WindowPlacement placement);

    [LibraryImport("user32", EntryPoint = "SetWindowPlacement")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPlacement(nint window, in WindowPlacement placement);

    [LibraryImport("user32", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PeekMessage(
        out WindowMessage message,
        nint window,
        uint minimum,
        uint maximum,
        uint removeMessage);

    [LibraryImport("user32", EntryPoint = "MsgWaitForMultipleObjectsEx", SetLastError = true)]
    private static unsafe partial uint MsgWaitForMultipleObjectsEx(
        uint count,
        nint* handles,
        uint milliseconds,
        uint wakeMask,
        uint flags);

    [LibraryImport("user32", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(in WindowMessage message);

    [LibraryImport("user32", EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessage(in WindowMessage message);

    [LibraryImport("user32", EntryPoint = "PostQuitMessage")]
    internal static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32", EntryPoint = "BeginPaint")]
    internal static partial nint BeginPaint(nint window, out PaintStruct paint);

    [LibraryImport("user32", EntryPoint = "EndPaint")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EndPaint(nint window, in PaintStruct paint);

    [LibraryImport("user32", EntryPoint = "GetClientRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(nint window, out NativeRect rect);

    [LibraryImport("user32", EntryPoint = "GetDpiForWindow")]
    internal static partial uint GetDpiForWindow(nint window);

    [LibraryImport("user32", EntryPoint = "ScreenToClient")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ScreenToClient(nint window, ref NativePoint point);

    [LibraryImport("user32", EntryPoint = "SetCapture")]
    internal static partial nint SetCapture(nint window);

    [LibraryImport("user32", EntryPoint = "ReleaseCapture")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReleaseCapture();

    [LibraryImport("user32", EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPosition(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [LibraryImport("user32", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static partial nint SetWindowLongPointer(nint window, int index, nint value);

    [LibraryImport("user32", EntryPoint = "GetWindowLongPtrW")]
    internal static partial nint GetWindowLongPointer(nint window, int index);

    [LibraryImport("user32", EntryPoint = "LoadCursorW", SetLastError = true)]
    internal static partial nint LoadCursor(nint instance, nint cursorName);

    [LibraryImport("user32", EntryPoint = "SetCursor")]
    internal static partial nint SetCursor(nint cursor);

    [LibraryImport("user32", EntryPoint = "LoadIconW", SetLastError = true)]
    internal static partial nint LoadIcon(nint instance, nint iconName);

    [LibraryImport("shell32", EntryPoint = "DragAcceptFiles")]
    internal static partial void DragAcceptFiles(nint window, [MarshalAs(UnmanagedType.Bool)] bool accept);

    [LibraryImport("shell32", EntryPoint = "DragQueryFileW")]
    private static unsafe partial uint DragQueryFile(nint dropHandle, uint index, char* fileName, uint capacity);

    [LibraryImport("shell32", EntryPoint = "DragFinish")]
    internal static partial void DragFinish(nint dropHandle);
}

internal enum ComApartment : uint
{
    MultiThreaded = 0x0,
    ApartmentThreaded = 0x2,
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WindowClass
{
    internal uint Size;
    internal uint Style;
    internal delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> WindowProcedure;
    internal int ClassExtraBytes;
    internal int WindowExtraBytes;
    internal nint Instance;
    internal nint Icon;
    internal nint Cursor;
    internal nint BackgroundBrush;
    internal char* MenuName;
    internal char* ClassName;
    internal nint SmallIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowMessage
{
    internal nint Window;
    internal uint Message;
    internal nuint WParam;
    internal nint LParam;
    internal uint Time;
    internal NativePoint Point;
    internal uint Private;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    internal int X;
    internal int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    internal int Left;
    internal int Top;
    internal int Right;
    internal int Bottom;

    internal readonly int Width => Right - Left;
    internal readonly int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PaintStruct
{
    internal nint DeviceContext;
    internal int Erase;
    internal NativeRect Paint;
    internal int Restore;
    internal int Update;
    internal fixed byte Reserved[32];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct CreateStruct
{
    internal void* CreateParameters;
    internal nint Instance;
    internal nint Menu;
    internal nint Parent;
    internal int Height;
    internal int Width;
    internal int Y;
    internal int X;
    internal int Style;
    internal char* Name;
    internal char* ClassName;
    internal uint ExtendedStyle;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowPlacement
{
    internal uint Length;
    internal uint Flags;
    internal uint ShowCommand;
    internal NativePoint MinPosition;
    internal NativePoint MaxPosition;
    internal NativeRect NormalPosition;
}

