using System.Drawing;

namespace Dameview.UI;

internal interface IUiElement
{
    public bool Update(in UiUpdateContext context)
    {
        return false;
    }

    public void Draw(in UiDrawContext context, SizeF size);

    public UiPointerResult HandlePointer(in UiPointerEvent input, SizeF size);
}

internal readonly record struct UiUpdateContext(double ElapsedSeconds);

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

internal readonly record struct UiPointerResult(
    bool Consumed = false,
    bool NeedsRepaint = false,
    bool CapturePointer = false);
