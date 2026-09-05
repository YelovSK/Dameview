using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Dameview.UI;

namespace Dameview.Platform;

internal sealed unsafe class AppWindow : IDisposable
{
    private const nint CursorArrow = 32512;
    private const nint CursorHand = 32649;
    private const nint CursorSizeWestEast = 32644;
    private const nint CursorSizeNorthSouth = 32645;
    private const string WindowClassName = "Dameview.MainWindow";
    private const int ApplicationIconResourceId = 32512;
    private const int WindowUserData = -21;
    private const uint SetWindowNoActivate = 0x0010;
    private const uint SetWindowNoZOrder = 0x0004;

    private readonly ConcurrentQueue<Action> _postedActions = new();
    private Timer? _repaintTimer;
    private GCHandle _selfHandle;
    private Exception? _unhandledException;
    private bool _frameRequested;
    private int _initialShowCommand = NativeMethods.ShowNormal;
    private WindowPlacementState? _lastPlacement;
    private UiCursor _cursor = UiCursor.Default;

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

    internal event Action? RenderFrame;
    internal event Action<int, int>? Resized;
    internal event Action<float>? DpiChanged;
    internal event Action<string>? FileDropped;
    internal event Action<UiKeyEvent>? KeyPressed;
    internal event Action<UiPointerEvent>? PointerInput;

    internal nint Handle { get; private set; }
    internal int ClientWidth { get; private set; }
    internal int ClientHeight { get; private set; }
    internal float Dpi { get; private set; }

    internal void SetCursor(UiCursor cursor)
    {
        _cursor = cursor;
        nint cursorHandle = NativeMethods.LoadCursor(0, cursor switch
        {
            UiCursor.Pointer => CursorHand,
            UiCursor.ResizeHorizontal => CursorSizeWestEast,
            UiCursor.ResizeVertical => CursorSizeNorthSouth,
            _ => CursorArrow,
        });
        _ = NativeMethods.SetCursor(cursorHandle);
    }

    internal void SetTitleBarTheme(bool dark)
    {
        int useDarkMode = dark ? 1 : 0;
        int captionColor = dark
            ? ToColorRef(9, 10, 12)
            : ToColorRef(235, 237, 242);
        int textColor = dark
            ? ToColorRef(240, 242, 247)
            : ToColorRef(23, 28, 38);

        NativeMethods.SetDwmWindowAttribute(
            Handle,
            NativeMethods.DwmUseImmersiveDarkMode,
            useDarkMode);
        NativeMethods.SetDwmWindowAttribute(
            Handle,
            NativeMethods.DwmCaptionColor,
            captionColor);
        NativeMethods.SetDwmWindowAttribute(
            Handle,
            NativeMethods.DwmTextColor,
            textColor);
    }

    internal void SetTitle(string title)
    {
        if (Handle != 0)
        {
            _ = NativeMethods.SetWindowText(Handle, title);
        }
    }

    internal void Close()
    {
        if (Handle != 0)
        {
            NativeMethods.DestroyWindow(Handle);
        }
    }

    internal void RestorePlacement(WindowPlacementState placement)
    {
        if (!placement.IsUsable)
        {
            return;
        }

        WindowPlacement native = new()
        {
            Length = (uint)Marshal.SizeOf<WindowPlacement>(),
            ShowCommand = placement.Maximized ? (uint)NativeMethods.ShowMaximized : (uint)NativeMethods.ShowNormal,
            NormalPosition = new NativeRect
            {
                Left = placement.X,
                Top = placement.Y,
                Right = placement.X + placement.Width,
                Bottom = placement.Y + placement.Height,
            },
        };

        if (NativeMethods.SetWindowPlacement(Handle, in native))
        {
            _initialShowCommand = (int)native.ShowCommand;
        }
    }

    internal WindowPlacementState CapturePlacement()
    {
        if (Handle == 0)
        {
            return _lastPlacement ?? new WindowPlacementState
            {
                Width = ClientWidth,
                Height = ClientHeight,
            };
        }

        return CapturePlacement(Handle);
    }

    private static WindowPlacementState CapturePlacement(nint window)
    {
        WindowPlacement native = new() { Length = (uint)Marshal.SizeOf<WindowPlacement>() };
        if (!NativeMethods.GetWindowPlacement(window, ref native))
        {
            return new WindowPlacementState
            {
                Width = 0,
                Height = 0,
            };
        }

        NativeRect normal = native.NormalPosition;
        return new WindowPlacementState
        {
            X = normal.Left,
            Y = normal.Top,
            Width = normal.Width,
            Height = normal.Height,
            Maximized = native.ShowCommand == NativeMethods.ShowMaximized,
        };
    }

    internal void RequestRepaint()
    {
        if (_frameRequested)
        {
            return;
        }

        _frameRequested = true;
        if (Handle != 0)
        {
            _ = NativeMethods.PostMessage(Handle, NativeMethods.MessageRenderFrame, 0, 0);
        }
    }

    internal void RequestRepaintAfter(TimeSpan delay)
    {
        if (Handle == 0)
        {
            return;
        }

        _repaintTimer ??= new Timer(
            static state =>
            {
                AppWindow window = (AppWindow)state!;
                window.Post(window.RequestRepaint);
            },
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _repaintTimer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    internal void Post(Action action)
    {
        nint window = Handle;
        if (window == 0)
        {
            return;
        }

        _postedActions.Enqueue(action);
        _ = NativeMethods.PostMessage(window, NativeMethods.MessageDispatch, 0, 0);
    }

    internal int Run(nint frameLatencyWaitHandle)
    {
        RequestRepaint();
        RenderRequestedFrame();
        NativeMethods.ShowWindow(Handle, _initialShowCommand);

        bool quit = false;
        while (!quit)
        {
            while (NativeMethods.PeekMessage(out WindowMessage message, 0, 0, 0, NativeMethods.RemoveMessage))
            {
                if (message.Message == NativeMethods.MessageQuit)
                {
                    quit = true;
                    break;
                }

                NativeMethods.TranslateMessage(in message);
                NativeMethods.DispatchMessage(in message);
            }

            if (quit)
            {
                break;
            }

            uint waitResult = NativeMethods.WaitForMessageOrHandle(
                frameLatencyWaitHandle,
                _frameRequested);
            if (waitResult == NativeMethods.WaitFailed)
            {
                throw NativeMethods.CreateLastErrorException("Could not wait for a window message or frame.");
            }

            if (_frameRequested && waitResult == NativeMethods.WaitObject0)
            {
                RenderRequestedFrame();
            }
        }

        if (_unhandledException is not null)
        {
            ExceptionDispatchInfo.Capture(_unhandledException).Throw();
        }

        return 0;
    }

    public void Dispose()
    {
        _repaintTimer?.Dispose();
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
                Style = NativeMethods.ClassHorizontalRedraw
                    | NativeMethods.ClassVerticalRedraw
                    | NativeMethods.ClassDoubleClicks,
                WindowProcedure = &WindowProcedure,
                Instance = instance,
                Icon = NativeMethods.LoadIcon(instance, new nint(ApplicationIconResourceId)),
                Cursor = NativeMethods.LoadCursor(0, new nint(32512)),
                ClassName = className,
            };

            windowClass.SmallIcon = windowClass.Icon;

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
                NativeMethods.EndPaint(window, in paint);
                _frameRequested = true;
                return 0;

            case NativeMethods.MessageRenderFrame:
                return 0;

            case NativeMethods.MessageDispatch:
                while (_postedActions.TryDequeue(out Action? action))
                {
                    action();
                }

                return 0;

            case NativeMethods.MessageEraseBackground:
                return 1;

            case NativeMethods.MessageSetCursor:
                SetCursor(_cursor);
                return 1;

            case NativeMethods.MessageKeyDown:
                KeyPressed?.Invoke(new UiKeyEvent(
                    (UiKey)wParam,
                    NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0,
                    NativeMethods.GetKeyState(NativeMethods.VirtualKeyControl) < 0));
                return 0;

            case NativeMethods.MessageLeftButtonDown:
                _ = NativeMethods.SetCapture(window);
                PointerInput?.Invoke(new UiPointerEvent(
                    UiPointerEventKind.Pressed,
                    new PointF(GetX(lParam), GetY(lParam)),
                    PointerButton.Primary));
                return 0;

            case NativeMethods.MessageMouseMove:
                PointerInput?.Invoke(new UiPointerEvent(
                    UiPointerEventKind.Moved,
                    new PointF(GetX(lParam), GetY(lParam))));
                return 0;

            case NativeMethods.MessageLeftButtonUp:
                PointerInput?.Invoke(new UiPointerEvent(
                    UiPointerEventKind.Released,
                    new PointF(GetX(lParam), GetY(lParam)),
                    PointerButton.Primary));
                _ = NativeMethods.ReleaseCapture();
                return 0;

            case NativeMethods.MessageCaptureChanged:
                PointerInput?.Invoke(new UiPointerEvent(
                    UiPointerEventKind.Cancelled,
                    PointF.Empty));
                return 0;

            case NativeMethods.MessageLeftButtonDoubleClick:
                PointerInput?.Invoke(new UiPointerEvent(
                    UiPointerEventKind.DoubleClicked,
                    new PointF(GetX(lParam), GetY(lParam)),
                    PointerButton.Primary));
                return 0;

            case NativeMethods.MessageMouseWheel:
                var wheelPoint = new NativePoint
                {
                    X = GetX(lParam),
                    Y = GetY(lParam),
                };
                _ = NativeMethods.ScreenToClient(window, ref wheelPoint);
                PointerInput?.Invoke(new UiPointerEvent(
                    UiPointerEventKind.Wheel,
                    new PointF(wheelPoint.X, wheelPoint.Y),
                    WheelDelta: GetHighWord(wParam)));
                return 0;

            case NativeMethods.MessageSize:
                ClientWidth = unchecked((ushort)(long)lParam);
                ClientHeight = unchecked((ushort)((long)lParam >> 16));
                Resized?.Invoke(ClientWidth, ClientHeight);
                RenderRequestedFrame();
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
                WindowPlacementState placement = CapturePlacement(window);
                if (placement.IsUsable)
                {
                    _lastPlacement = placement;
                }

                Handle = 0;
                _postedActions.Clear();
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

    private void RenderRequestedFrame()
    {
        if (!_frameRequested)
        {
            return;
        }

        _frameRequested = false;
        RenderFrame?.Invoke();
    }

    private static int GetX(nint value)
    {
        return unchecked((short)(long)value);
    }

    private static int GetY(nint value)
    {
        return unchecked((short)((long)value >> 16));
    }

    private static int GetHighWord(nuint value)
    {
        return unchecked((short)((ulong)value >> 16));
    }

    private static int ToColorRef(byte red, byte green, byte blue)
    {
        return red | (green << 8) | (blue << 16);
    }
}

