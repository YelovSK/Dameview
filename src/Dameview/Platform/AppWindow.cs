using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Dameview.UI;
using Vortice.Mathematics;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.PInvoke;

namespace Dameview.Platform;

internal sealed unsafe class AppWindow : IDisposable
{
    private const string WindowClassName = "Dameview.MainWindow";
    private static bool _windowClassRegistered;

    private readonly ConcurrentQueue<Action> _postedActions = new();
    private Timer? _repaintTimer;
    private GCHandle _selfHandle;
    private Exception? _unhandledException;
    private bool _frameRequested;
    private SHOW_WINDOW_CMD _initialShowCommand = SHOW_WINDOW_CMD.SW_SHOWNORMAL;
    private WindowPlacementState? _lastPlacement;
    private UiCursor _cursor = UiCursor.Default;

    internal AppWindow(string title, int width, int height)
    {
        nint instance = GetModuleHandle(default(PCWSTR));
        if (instance == 0)
        {
            throw NativeMethods.CreateLastErrorException("Could not get the application module handle.");
        }

        if (!_windowClassRegistered)
        {
            RegisterWindowClass(instance);
            _windowClassRegistered = true;
        }

        _selfHandle = GCHandle.Alloc(this);
        Handle = CreateWindow(instance, title, width, height);

        if (Handle == 0)
        {
            _selfHandle.Free();
            throw NativeMethods.CreateLastErrorException("Could not create the main window.");
        }

        UpdateClientSize();
        Dpi = GetDpiForWindow((HWND)Handle);
        DragAcceptFiles((HWND)Handle, true);
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

    internal void ApplyCursor(UiCursor cursor)
    {
        _cursor = cursor;
        PCWSTR cursorName = cursor switch
        {
            UiCursor.Pointer => IDC_HAND,
            UiCursor.ResizeHorizontal => IDC_SIZEWE,
            UiCursor.ResizeVertical => IDC_SIZENS,
            _ => IDC_ARROW,
        };
        HCURSOR cursorHandle = LoadCursor(default, cursorName);
        _ = SetCursor(cursorHandle);
    }

    internal void SetTitleBarTheme(bool dark, Color4 captionColor, Color4 textColor)
    {
        NativeMethods.SetDwmWindowAttribute(
            (HWND)Handle,
            DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE,
            dark ? 1 : 0);
        NativeMethods.SetDwmWindowAttribute(
            (HWND)Handle,
            DWMWINDOWATTRIBUTE.DWMWA_CAPTION_COLOR,
            ToColorRef(captionColor));
        NativeMethods.SetDwmWindowAttribute(
            (HWND)Handle,
            DWMWINDOWATTRIBUTE.DWMWA_TEXT_COLOR,
            ToColorRef(textColor));
    }

    internal void SetTitle(string title)
    {
        if (Handle != 0)
        {
            _ = SetWindowText((HWND)Handle, title);
        }
    }

    internal void CenterOnPrimaryMonitor()
    {
        HMONITOR monitor = MonitorFromPoint(default, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        MONITORINFO monitorInfo = new() { cbSize = (uint)sizeof(MONITORINFO) };
        if (monitor.IsNull
            || !GetMonitorInfo(monitor, ref monitorInfo)
            || !GetWindowRect((HWND)Handle, out RECT windowBounds))
        {
            return;
        }

        RECT workArea = monitorInfo.rcWork;
        int x = workArea.left + (workArea.Width - windowBounds.Width) / 2;
        int y = workArea.top + (workArea.Height - windowBounds.Height) / 2;
        SetWindowPos(
            (HWND)Handle,
            default,
            x,
            y,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
                | SET_WINDOW_POS_FLAGS.SWP_NOZORDER
                | SET_WINDOW_POS_FLAGS.SWP_NOSIZE);
    }

    internal void Close()
    {
        if (Handle != 0)
        {
            DestroyWindow((HWND)Handle);
        }
    }

    internal void RestorePlacement(WindowPlacementState placement)
    {
        if (!placement.IsUsable)
        {
            return;
        }

        WINDOWPLACEMENT native = new()
        {
            length = (uint)sizeof(WINDOWPLACEMENT),
            showCmd = placement.Maximized ? SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED : SHOW_WINDOW_CMD.SW_SHOWNORMAL,
            rcNormalPosition = new RECT(
                placement.X,
                placement.Y,
                placement.X + placement.Width,
                placement.Y + placement.Height),
        };

        if (SetWindowPlacement((HWND)Handle, in native))
        {
            _initialShowCommand = native.showCmd;
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
        WINDOWPLACEMENT native = new() { length = (uint)sizeof(WINDOWPLACEMENT) };
        if (!GetWindowPlacement((HWND)window, ref native))
        {
            return new WindowPlacementState
            {
                Width = 0,
                Height = 0,
            };
        }

        RECT normal = native.rcNormalPosition;
        return new WindowPlacementState
        {
            X = normal.left,
            Y = normal.top,
            Width = normal.Width,
            Height = normal.Height,
            Maximized = native.showCmd == SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED,
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
            _ = PostMessage((HWND)Handle, NativeMethods.MessageRenderFrame, 0, 0);
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
                var window = (AppWindow)state!;
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
        _ = PostMessage((HWND)window, NativeMethods.MessageDispatch, 0, 0);
    }

    internal int Run(nint frameLatencyWaitHandle)
    {
        RequestRepaint();
        RenderRequestedFrame();
        ShowWindow((HWND)Handle, _initialShowCommand);

        bool quit = false;
        while (!quit)
        {
            while (PeekMessage(out MSG message, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
            {
                if (message.message == WM_QUIT)
                {
                    quit = true;
                    break;
                }

                TranslateMessage(in message);
                DispatchMessage(in message);
            }

            if (quit)
            {
                break;
            }

            WAIT_EVENT waitResult = NativeMethods.WaitForMessageOrHandle(
                frameLatencyWaitHandle,
                _frameRequested);
            if (waitResult == WAIT_EVENT.WAIT_FAILED)
            {
                throw NativeMethods.CreateLastErrorException("Could not wait for a window message or frame.");
            }

            if (_frameRequested && waitResult == WAIT_EVENT.WAIT_OBJECT_0)
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
            DestroyWindow((HWND)Handle);
            Handle = 0;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private nint CreateWindow(nint instance, string title, int width, int height)
    {
        fixed (char* className = WindowClassName)
        fixed (char* windowTitle = title)
        {
            HWND window = CreateWindowEx(
                default,
                className,
                windowTitle,
                WINDOW_STYLE.WS_OVERLAPPEDWINDOW,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                width,
                height,
                default,
                default,
                (HINSTANCE)instance,
                (void*)GCHandle.ToIntPtr(_selfHandle));

            return window;
        }
    }

    private static void RegisterWindowClass(nint instance)
    {
        fixed (char* className = WindowClassName)
        {
            WNDCLASSEXW windowClass = new()
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                style = WNDCLASS_STYLES.CS_HREDRAW
                    | WNDCLASS_STYLES.CS_VREDRAW
                    | WNDCLASS_STYLES.CS_DBLCLKS,
                lpfnWndProc = &WindowProcedure,
                hInstance = (HINSTANCE)instance,
                hIcon = LoadIcon((HINSTANCE)instance, IDI_APPLICATION),
                hCursor = LoadCursor(default, IDC_ARROW),
                lpszClassName = className,
            };

            windowClass.hIconSm = windowClass.hIcon;

            if (RegisterClassEx(in windowClass) == 0)
            {
                throw NativeMethods.CreateLastErrorException("Could not register the window class.");
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        AppWindow? appWindow = GetWindow(window, message, lParam);
        if (appWindow is null)
        {
            return DefWindowProc(window, message, wParam, lParam);
        }

        try
        {
            return appWindow.ProcessMessage(window, message, wParam, lParam);
        }
        catch (Exception exception)
        {
            appWindow._unhandledException = exception;
            DestroyWindow(window);
            return default;
        }
    }

    private static AppWindow? GetWindow(HWND window, uint message, LPARAM lParam)
    {
        nint handle;

        if (message == WM_NCCREATE)
        {
            var create = (CREATESTRUCTW*)(nint)lParam;
            handle = (nint)create->lpCreateParams;
            SetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA, handle);
        }
        else
        {
            handle = GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA);
        }

        return handle == 0 ? null : GCHandle.FromIntPtr(handle).Target as AppWindow;
    }

    private LRESULT ProcessMessage(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case WM_PAINT:
                BeginPaint(window, out PAINTSTRUCT paint);
                EndPaint(window, in paint);
                _frameRequested = true;
                return default;

            case NativeMethods.MessageRenderFrame:
                return default;

            case NativeMethods.MessageDispatch:
                while (_postedActions.TryDequeue(out Action? action))
                {
                    action();
                }

                return default;

            case WM_ERASEBKGND:
                return (LRESULT)1;

            case WM_SETCURSOR when GetLowWord(lParam) == HTCLIENT:
                ApplyCursor(_cursor);
                return (LRESULT)1;

            case WM_KEYDOWN:
                KeyPressed?.Invoke(new UiKeyEvent(
                    (UiKey)(nuint)wParam,
                    GetKeyState((int)VIRTUAL_KEY.VK_SHIFT) < 0,
                    GetKeyState((int)VIRTUAL_KEY.VK_CONTROL) < 0));
                return default;

            case WM_LBUTTONDOWN:
                _ = SetCapture(window);
                PointerInput?.Invoke(new UiPointerEvent(
                    UiPointerEventKind.Pressed,
                    new PointF(GetX(lParam), GetY(lParam)),
                    PointerButton.Primary));
                return default;

            case WM_MOUSEMOVE:
                PointerInput?.Invoke(new UiPointerEvent(
                    UiPointerEventKind.Moved,
                    new PointF(GetX(lParam), GetY(lParam))));
                return default;

            case WM_LBUTTONUP:
                PointerInput?.Invoke(new UiPointerEvent(
                    UiPointerEventKind.Released,
                    new PointF(GetX(lParam), GetY(lParam)),
                    PointerButton.Primary));
                _ = ReleaseCapture();
                return default;

            case WM_CAPTURECHANGED:
                PointerInput?.Invoke(new UiPointerEvent(
                    UiPointerEventKind.Cancelled,
                    PointF.Empty));
                return default;

            case WM_LBUTTONDBLCLK:
                PointerInput?.Invoke(new UiPointerEvent(
                    UiPointerEventKind.DoubleClicked,
                    new PointF(GetX(lParam), GetY(lParam)),
                    PointerButton.Primary));
                return default;

            case WM_MBUTTONDOWN:
            case WM_MBUTTONDBLCLK:
                PointerInput?.Invoke(new UiPointerEvent(
                    UiPointerEventKind.Pressed,
                    new PointF(GetX(lParam), GetY(lParam)),
                    PointerButton.Middle));
                return default;

            case WM_MOUSEWHEEL:
                var wheelPoint = new Point(GetX(lParam), GetY(lParam));
                _ = ScreenToClient(window, ref wheelPoint);
                PointerInput?.Invoke(new UiPointerEvent(
                    UiPointerEventKind.Wheel,
                    new PointF(wheelPoint.X, wheelPoint.Y),
                    WheelDelta: GetHighWord(wParam)));
                return default;

            case WM_SIZE:
                ClientWidth = unchecked((ushort)(long)lParam);
                ClientHeight = unchecked((ushort)((long)lParam >> 16));
                Resized?.Invoke(ClientWidth, ClientHeight);
                RenderRequestedFrame();
                return default;

            case WM_DPICHANGED:
                Dpi = unchecked((ushort)(ulong)wParam);
                var suggested = (RECT*)(nint)lParam;
                SetWindowPos(
                    window,
                    default,
                    suggested->left,
                    suggested->top,
                    suggested->Width,
                    suggested->Height,
                    SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
                DpiChanged?.Invoke(Dpi);
                return default;

            case WM_DROPFILES:
                nint dropHandle = (nint)(nuint)wParam;
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
                    DragFinish((HDROP)dropHandle);
                }

                return default;

            case WM_DESTROY:
                WindowPlacementState placement = CapturePlacement(window);
                if (placement.IsUsable)
                {
                    _lastPlacement = placement;
                }

                Handle = 0;
                _postedActions.Clear();
                PostQuitMessage(0);
                return default;

            default:
                return DefWindowProc(window, message, wParam, lParam);
        }
    }

    private void UpdateClientSize()
    {
        if (!GetClientRect((HWND)Handle, out RECT clientRect))
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

    private static int GetLowWord(nint value)
    {
        return unchecked((short)(long)value);
    }

    private static int GetHighWord(nuint value)
    {
        return unchecked((short)((ulong)value >> 16));
    }

    private static int ToColorRef(byte red, byte green, byte blue)
    {
        return red | (green << 8) | (blue << 16);
    }

    private static int ToColorRef(Color4 color)
    {
        return ToColorRef(
            (byte)MathF.Round(color.R * 255f),
            (byte)MathF.Round(color.G * 255f),
            (byte)MathF.Round(color.B * 255f));
    }
}
