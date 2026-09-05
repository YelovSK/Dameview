namespace Dameview.UI;

// Values match Win32 virtual-key codes so AppWindow can pass through keys
// that are not handled by the UI yet.
internal enum UiKey : uint
{
    Tab = 0x09,
    Enter = 0x0D,
    Escape = 0x1B,
    Space = 0x20,
    Left = 0x25,
    Up = 0x26,
    Right = 0x27,
    Down = 0x28,
    Number1 = 0x31,
    F = 0x46,
    Numpad1 = 0x61,
}

internal readonly record struct UiKeyEvent(UiKey Key, bool Shift = false);
