using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Dameview.Platform;

internal sealed unsafe class AppWindow : IDisposable
{
    private const string WindowClassName = "Dameview.MainWindow";
    private const int WindowUserData = -21;
    private const uint SetWindowNoActivate = 0x0010;
    private const uint SetWindowNoZOrder = 0x0004;

    private GCHandle _selfHandle;
    private Exception? _unhandledException;

    internal AppWindow(string title, int width, int height)
    {
        nint instance = NativeMethods.GetModuleHandle(null);
        if (instance == 0)
        {
            throw NativeMethods.CreateLastErrorException("Could not get the application module handle.");
        }

        RegisterWindowClass(instance);

        _selfHandle = GCHandle.Alloc(this);
        Handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            title,
            NativeMethods.WindowStyleOverlappedWindow,
            NativeMethods.UseDefault,
            NativeMethods.UseDefault,
            width,
            height,
            0,
            0,
            instance,
            GCHandle.ToIntPtr(_selfHandle));

        if (Handle == 0)
        {
            _selfHandle.Free();
            throw NativeMethods.CreateLastErrorException("Could not create the main window.");
        }

        UpdateClientSize();
        Dpi = NativeMethods.GetDpiForWindow(Handle);
        NativeMethods.DragAcceptFiles(Handle, true);
    }

    internal event Action? Paint;
    internal event Action<int, int>? Resized;
    internal event Action<float>? DpiChanged;
    internal event Action<string>? FileDropped;
    internal event Action<uint>? KeyPressed;

    internal nint Handle { get; private set; }
    internal int ClientWidth { get; private set; }
    internal int ClientHeight { get; private set; }
    internal float Dpi { get; private set; }

    internal void RequestRepaint()
    {
        _ = NativeMethods.InvalidateRect(Handle, 0, false);
    }

    internal int Run()
    {
        NativeMethods.ShowWindow(Handle, NativeMethods.ShowNormal);
        if (!NativeMethods.UpdateWindow(Handle))
        {
            throw NativeMethods.CreateLastErrorException("Could not draw the main window.");
        }

        while (true)
        {
            int result = NativeMethods.GetMessage(out WindowMessage message, 0, 0, 0);
            if (result == -1)
            {
                throw NativeMethods.CreateLastErrorException("Could not read a window message.");
            }

            if (result == 0)
            {
                break;
            }

            NativeMethods.TranslateMessage(in message);
            NativeMethods.DispatchMessage(in message);
        }

        if (_unhandledException is not null)
        {
            ExceptionDispatchInfo.Capture(_unhandledException).Throw();
        }

        return 0;
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            NativeMethods.DestroyWindow(Handle);
            Handle = 0;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private static void RegisterWindowClass(nint instance)
    {
        fixed (char* className = WindowClassName)
        {
            WindowClass windowClass = new()
            {
                Size = (uint)sizeof(WindowClass),
                Style = NativeMethods.ClassHorizontalRedraw | NativeMethods.ClassVerticalRedraw,
                WindowProcedure = &WindowProcedure,
                Instance = instance,
                Cursor = NativeMethods.LoadCursor(0, new nint(32512)),
                ClassName = className,
            };

            if (NativeMethods.RegisterClass(ref windowClass) == 0)
            {
                throw NativeMethods.CreateLastErrorException("Could not register the window class.");
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        AppWindow? appWindow = GetWindow(window, message, lParam);
        if (appWindow is null)
        {
            return NativeMethods.DefWindowProc(window, message, wParam, lParam);
        }

        try
        {
            return appWindow.ProcessMessage(window, message, wParam, lParam);
        }
        catch (Exception exception)
        {
            appWindow._unhandledException = exception;
            NativeMethods.DestroyWindow(window);
            return 0;
        }
    }

    private static AppWindow? GetWindow(nint window, uint message, nint lParam)
    {
        nint handle;

        if (message == NativeMethods.MessageNonClientCreate)
        {
            var create = (CreateStruct*)lParam;
            handle = (nint)create->CreateParameters;
            NativeMethods.SetWindowLongPointer(window, WindowUserData, handle);
        }
        else
        {
            handle = NativeMethods.GetWindowLongPointer(window, WindowUserData);
        }

        return handle == 0 ? null : GCHandle.FromIntPtr(handle).Target as AppWindow;
    }

    private nint ProcessMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case NativeMethods.MessagePaint:
                NativeMethods.BeginPaint(window, out PaintStruct paint);
                try
                {
                    Paint?.Invoke();
                }
                finally
                {
                    NativeMethods.EndPaint(window, in paint);
                }

                return 0;

            case NativeMethods.MessageEraseBackground:
                return 1;

            case NativeMethods.MessageKeyDown:
                KeyPressed?.Invoke((uint)wParam);
                return 0;

            case NativeMethods.MessageSize:
                ClientWidth = unchecked((ushort)(long)lParam);
                ClientHeight = unchecked((ushort)((long)lParam >> 16));
                Resized?.Invoke(ClientWidth, ClientHeight);
                return 0;

            case NativeMethods.MessageDpiChanged:
                Dpi = unchecked((ushort)(ulong)wParam);
                var suggested = (NativeRect*)lParam;
                NativeMethods.SetWindowPosition(
                    window,
                    0,
                    suggested->Left,
                    suggested->Top,
                    suggested->Width,
                    suggested->Height,
                    SetWindowNoActivate | SetWindowNoZOrder);
                DpiChanged?.Invoke(Dpi);
                return 0;

            case NativeMethods.MessageDropFiles:
                nint dropHandle = (nint)wParam;
                try
                {
                    string path = NativeMethods.GetDroppedFilePath(dropHandle);
                    if (path.Length > 0)
                    {
                        FileDropped?.Invoke(path);
                    }
                }
                finally
                {
                    NativeMethods.DragFinish(dropHandle);
                }

                return 0;

            case NativeMethods.MessageDestroy:
                Handle = 0;
                NativeMethods.PostQuitMessage(0);
                return 0;

            default:
                return NativeMethods.DefWindowProc(window, message, wParam, lParam);
        }
    }

    private void UpdateClientSize()
    {
        if (!NativeMethods.GetClientRect(Handle, out NativeRect clientRect))
        {
            throw NativeMethods.CreateLastErrorException("Could not get the client size.");
        }

        ClientWidth = clientRect.Width;
        ClientHeight = clientRect.Height;
    }
}

