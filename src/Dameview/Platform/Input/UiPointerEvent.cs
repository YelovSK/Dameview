using System.Drawing;

namespace Dameview.Platform;

internal readonly record struct UiPointerEvent(
    UiPointerEventKind Kind,
    PointF Position,
    PointerButton Button = PointerButton.None,
    int WheelDelta = 0)
{
    internal UiPointerEvent ToLocal(RectangleF bounds)
    {
        return this with
        {
            Position = new PointF(
                Position.X - bounds.X,
                Position.Y - bounds.Y),
        };
    }
}

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
