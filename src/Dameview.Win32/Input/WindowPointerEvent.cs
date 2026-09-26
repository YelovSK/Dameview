using System.Drawing;

namespace Dameview.Win32.Input;

internal readonly record struct WindowPointerEvent(
    WindowPointerEventKind Kind,
    PointF Position,
    PointerButton Button = PointerButton.None,
    int WheelDelta = 0);

internal enum WindowPointerEventKind
{
    Moved,
    Left,
    Pressed,
    Released,
    Cancelled,
    DoubleClicked,
    Wheel,
}

internal enum PointerButton
{
    None,
    Primary,
    Secondary,
    Middle,
}
