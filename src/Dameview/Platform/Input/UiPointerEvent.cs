using System.Drawing;

namespace Dameview.Platform;

internal readonly record struct UiPointerEvent(
    UiPointerEventKind Kind,
    PointF Position,
    PointerButton Button = PointerButton.None,
    int WheelDelta = 0);

internal enum UiPointerEventKind
{
    Moved,
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
