namespace Dameview.Platform;

// Values match Win32 virtual-key codes so AppWindow can pass through keys
// that are not handled by the UI yet.
internal enum UiKey : uint
{
    Backspace = 0x08,
    Tab = 0x09,
    Enter = 0x0D,
    Escape = 0x1B,
    Space = 0x20,
    End = 0x23,
    Home = 0x24,
    Left = 0x25,
    Up = 0x26,
    Right = 0x27,
    Down = 0x28,
    Delete = 0x2E,
    Number1 = 0x31,
    F = 0x46,
    P = 0x50,
    S = 0x53,
    T = 0x54,
    W = 0x57,
    Numpad1 = 0x61,
    Comma = 0xBC,
}

internal readonly record struct UiKeyEvent(
    UiKey Key,
    bool Shift = false,
    bool Control = false);
