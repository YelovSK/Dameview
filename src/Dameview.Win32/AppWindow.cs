using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Dameview.Win32.Input;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.PInvoke;

namespace Dameview.Win32;

internal sealed unsafe class AppWindow : IDisposable
{
    internal static readonly string WindowClassName = typeof(AppWindow).FullName!;
    private static bool _windowClassRegistered;

    private readonly ConcurrentQueue<Action> _postedActions = new();
    private readonly Utf16TextInputDecoder _textInputDecoder = new();
    private Timer? _repaintTimer;
    private GCHandle _selfHandle;
    private Exception? _unhandledException;
    private bool _frameRequested;
    private WINDOWPLACEMENT? _windowedPlacement;
    private WINDOW_STYLE _windowedStyle;
    private SHOW_WINDOW_CMD _initialShowCommand = SHOW_WINDOW_CMD.SW_SHOWNORMAL;
    private WindowPlacementState? _lastPlacement;
    private WindowCursor _cursor = WindowCursor.Default;
    private FileDropTarget? _dropTarget;
    private bool _oleInitialized;

    /// <param name="placement">Where the window was last left, if it has been.</param>
    internal AppWindow(string title, int width, int height, WindowPlacementState? placement = null)
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
        Handle = CreateWindow(instance, title, width, height, placement);

        if (Handle == 0)
        {
            _selfHandle.Free();
            throw NativeMethods.CreateLastErrorException("Could not create the main window.");
        }

        UpdateClientSize();
        Dpi = GetDpiForWindow((HWND)Handle);
    }

    internal event Action? RenderFrame;
    internal event Action? Shown;

    /// <summary>The frame-latency object the loop waits on; it changes with the swap chain.</summary>
    internal nint FrameLatencyWaitHandle { get; set; }
    internal event Action? Closed;
    internal event Action<int, int>? Resized;
    internal event Action<float>? DpiChanged;
    internal event Action<WindowFileDragEvent>? FileDragInput;
    internal event Action<WindowKeyEvent>? KeyPressed;
    internal event Action<string>? TextInput;
    internal event Action<WindowPointerEvent>? PointerInput;
    internal event Action<nuint, string>? CopyDataReceived;

    internal nint Handle { get; private set; }
    internal int ClientWidth { get; private set; }
    internal int ClientHeight { get; private set; }
    internal float Dpi { get; private set; }
    internal bool IsFullscreen => _windowedPlacement.HasValue;

    internal void ApplyCursor(WindowCursor cursor)
    {
        _cursor = cursor;
        PCWSTR cursorName = cursor switch
        {
            WindowCursor.Pointer => IDC_HAND,
            WindowCursor.Text => IDC_IBEAM,
            WindowCursor.ResizeHorizontal => IDC_SIZEWE,
            WindowCursor.ResizeVertical => IDC_SIZENS,
            _ => IDC_ARROW,
        };
        HCURSOR cursorHandle = LoadCursor(default, cursorName);
        _ = SetCursor(cursorHandle);
    }

    internal void SetTitleBarTheme(bool dark, Color captionColor, Color textColor)
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

    internal bool Activate()
    {
        if (Handle != 0)
        {
            if (IsIconic((HWND)Handle))
            {
                ShowWindow((HWND)Handle, SHOW_WINDOW_CMD.SW_RESTORE);
            }

            return SetForegroundWindow((HWND)Handle);
        }

        return false;
    }

    internal void ToggleFullscreen()
    {
        if (_windowedPlacement is null)
        {
            EnterFullscreen();
        }
        else
        {
            ExitFullscreen();
        }
    }

    private void EnterFullscreen()
    {
        WINDOWPLACEMENT placement = new() { length = (uint)sizeof(WINDOWPLACEMENT) };
        HMONITOR monitor = MonitorFromWindow(
            (HWND)Handle,
            MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        MONITORINFO monitorInfo = new() { cbSize = (uint)sizeof(MONITORINFO) };
        if (!GetWindowPlacement((HWND)Handle, ref placement)
            || monitor.IsNull
            || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        _windowedPlacement = placement;
        _windowedStyle = (WINDOW_STYLE)(nuint)GetWindowLongPtr(
            (HWND)Handle,
            WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        _ = SetWindowLongPtr(
            (HWND)Handle,
            WINDOW_LONG_PTR_INDEX.GWL_STYLE,
            (nint)(nuint)(_windowedStyle & ~WINDOW_STYLE.WS_OVERLAPPEDWINDOW));

        RECT bounds = monitorInfo.rcMonitor;
        SetWindowPos(
            (HWND)Handle,
            default,
            bounds.left,
            bounds.top,
            bounds.Width,
            bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
                | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER
                | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
    }

    private void ExitFullscreen()
    {
        if (_windowedPlacement is not { } placement)
        {
            return;
        }

        _ = SetWindowLongPtr(
            (HWND)Handle,
            WINDOW_LONG_PTR_INDEX.GWL_STYLE,
            (nint)(nuint)_windowedStyle);
        _ = SetWindowPlacement((HWND)Handle, in placement);
        SetWindowPos(
            (HWND)Handle,
            default,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
                | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER
                | SET_WINDOW_POS_FLAGS.SWP_NOMOVE
                | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
                | SET_WINDOW_POS_FLAGS.SWP_NOZORDER
                | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
        _windowedPlacement = null;
    }

    internal void RestorePlacement(WindowPlacementState placement)
    {
        if (!placement.IsUsable)
        {
            return;
        }

        (int originX, int originY) = GetWorkAreaOrigin(Handle);
        int left = placement.X - originX;
        int top = placement.Y - originY;
        WINDOWPLACEMENT native = new()
        {
            length = (uint)sizeof(WINDOWPLACEMENT),
            showCmd = placement.Maximized ? SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED : SHOW_WINDOW_CMD.SW_SHOWNORMAL,
            rcNormalPosition = new RECT(
                left,
                top,
                left + placement.Width,
                top + placement.Height),
        };

        if (SetWindowPlacement((HWND)Handle, in native))
        {
            _initialShowCommand = native.showCmd;
        }
    }

    internal WindowPlacementState CapturePlacement()
    {
        if (_windowedPlacement is { } windowedPlacement)
        {
            return ToPlacementState(windowedPlacement, Handle);
        }

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

    private static (int X, int Y) GetWorkAreaOrigin(nint window)
    {
        HMONITOR monitor = MonitorFromWindow((HWND)window, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        MONITORINFO info = new() { cbSize = (uint)sizeof(MONITORINFO) };
        return monitor.IsNull || !GetMonitorInfo(monitor, ref info)
            ? (0, 0)
            : (info.rcWork.left, info.rcWork.top);
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

        return ToPlacementState(native, window);
    }

    /// <summary>
    /// Placement is stored in screen coordinates. Windows reports the restored rectangle
    /// relative to the monitor's work area but positions new windows in screen coordinates,
    /// so storing what it reports would move the window by the taskbar on every round trip.
    /// The monitor is only unambiguous while the window exists, so the conversion belongs
    /// here rather than where the placement is used.
    /// </summary>
    private static WindowPlacementState ToPlacementState(WINDOWPLACEMENT native, nint window)
    {
        RECT normal = native.rcNormalPosition;
        (int originX, int originY) = GetWorkAreaOrigin(window);
        return new WindowPlacementState
        {
            X = normal.left + originX,
            Y = normal.top + originY,
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
        FrameLatencyWaitHandle = frameLatencyWaitHandle;
        RequestRepaint();
        RenderRequestedFrame();
        ShowWindow((HWND)Handle, _initialShowCommand);
        Shown?.Invoke();

        // After the window is up: nothing can be dragged onto one that was never on
        // screen, and registering costs a COM apartment initialization that the window
        // does not have to wait behind.
        RegisterFileDropTarget();

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
                FrameLatencyWaitHandle,
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
            // Sends WM_DESTROY synchronously, which is where the drop target is revoked.
            DestroyWindow((HWND)Handle);
            Handle = 0;
        }

        if (_oleInitialized)
        {
            OleUninitialize();
            _oleInitialized = false;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private void RegisterFileDropTarget()
    {
        // RegisterDragDrop requires OleInitialize. It returns S_FALSE here because COM is
        // already initialized, which still needs a matching OleUninitialize.
        _oleInitialized = OleInitialize().Succeeded;
        var target = new FileDropTarget(Handle, input => FileDragInput?.Invoke(input));

        // Failing here only costs drag-and-drop, so the window still opens.
        if (RegisterDragDrop((HWND)Handle, target).Succeeded)
        {
            _dropTarget = target;
        }
    }

    // Created in the state it will be shown in, so the client size is final before anything
    // is built from it and the window is never restyled once it is on screen.
    private nint CreateWindow(
        nint instance,
        string title,
        int width,
        int height,
        WindowPlacementState? placement)
    {
        WINDOW_STYLE style = WINDOW_STYLE.WS_OVERLAPPEDWINDOW;
        int x = CW_USEDEFAULT;
        int y = CW_USEDEFAULT;
        if (placement is { IsUsable: true } saved)
        {
            x = saved.X;
            y = saved.Y;
            width = saved.Width;
            height = saved.Height;
            if (saved.Maximized)
            {
                style |= WINDOW_STYLE.WS_MAXIMIZE;
                _initialShowCommand = SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED;
            }
        }

        fixed (char* className = WindowClassName)
        fixed (char* windowTitle = title)
        {
            HWND window = CreateWindowEx(
                default,
                className,
                windowTitle,
                style,
                x,
                y,
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
            appWindow._unhandledException ??= exception;
            DestroyWindow(window);
            // A failed native callback must unwind the message loop even when no
            // application close handler is attached, or that handler itself failed.
            NativeMethods.RequestMessageLoopExit();
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
                KeyPressed?.Invoke(new WindowKeyEvent(
                    (WindowKey)(nuint)wParam,
                    GetKeyState((int)VIRTUAL_KEY.VK_SHIFT) < 0,
                    GetKeyState((int)VIRTUAL_KEY.VK_CONTROL) < 0));
                return default;

            case WM_CHAR:
                if (_textInputDecoder.Push((char)(nuint)wParam) is { } text)
                {
                    TextInput?.Invoke(text);
                }

                return default;

            case WM_LBUTTONDOWN:
                _ = SetCapture(window);
                PointerInput?.Invoke(new WindowPointerEvent(
                    WindowPointerEventKind.Pressed,
                    new PointF(GetX(lParam), GetY(lParam)),
                    PointerButton.Primary));
                return default;

            case WM_MOUSEMOVE:
                PointerInput?.Invoke(new WindowPointerEvent(
                    WindowPointerEventKind.Moved,
                    new PointF(GetX(lParam), GetY(lParam))));
                return default;

            case WM_LBUTTONUP:
                PointerInput?.Invoke(new WindowPointerEvent(
                    WindowPointerEventKind.Released,
                    new PointF(GetX(lParam), GetY(lParam)),
                    PointerButton.Primary));
                _ = ReleaseCapture();
                return default;

            case WM_CAPTURECHANGED:
                PointerInput?.Invoke(new WindowPointerEvent(
                    WindowPointerEventKind.Cancelled,
                    PointF.Empty));
                return default;

            case WM_LBUTTONDBLCLK:
                PointerInput?.Invoke(new WindowPointerEvent(
                    WindowPointerEventKind.DoubleClicked,
                    new PointF(GetX(lParam), GetY(lParam)),
                    PointerButton.Primary));
                return default;

            case WM_MBUTTONDOWN:
            case WM_MBUTTONDBLCLK:
                PointerInput?.Invoke(new WindowPointerEvent(
                    WindowPointerEventKind.Pressed,
                    new PointF(GetX(lParam), GetY(lParam)),
                    PointerButton.Middle));
                return default;

            case WM_MOUSEWHEEL:
                var wheelPoint = new Point(GetX(lParam), GetY(lParam));
                _ = ScreenToClient(window, ref wheelPoint);
                PointerInput?.Invoke(new WindowPointerEvent(
                    WindowPointerEventKind.Wheel,
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

            case WM_COPYDATA:
                var data = (WindowCopyData.Data*)(nint)lParam;
                if (data is null
                    || data->ByteCount % sizeof(char) != 0
                    || data->ByteCount > (uint)int.MaxValue * sizeof(char)
                    || (data->ByteCount > 0 && data->DataPointer == 0))
                {
                    return default;
                }

                int characterCount = checked((int)(data->ByteCount / sizeof(char)));
                string copyDataText = characterCount == 0
                    ? string.Empty
                    : new string((char*)data->DataPointer, 0, characterCount);
                CopyDataReceived?.Invoke(data->DataId, copyDataText);
                return (LRESULT)1;

            case WM_DESTROY:
                WindowPlacementState placement = CapturePlacement();
                if (placement.IsUsable)
                {
                    _lastPlacement = placement;
                }

                // OLE holds a reference to the drop target until the window revokes it,
                // and this is the last point where the handle is still valid.
                if (_dropTarget is not null)
                {
                    RevokeDragDrop(window);
                    _dropTarget = null;
                }

                Handle = 0;
                _frameRequested = false;
                _postedActions.Clear();
                Closed?.Invoke();
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

    private static int ToColorRef(Color color)
    {
        return color.R | (color.G << 8) | (color.B << 16);
    }
}
