using System.Drawing;

namespace Dameview.UI;

// Each container owns the capture of its immediate child.
internal sealed class UiPointerRouter
{
    internal IUiElement? Captured { get; private set; }

    internal UiPointerResult Route(
        IUiElement child,
        RectangleF bounds,
        in UiPointerEvent input,
        bool observeOutside = false)
    {
        if (!observeOutside && !bounds.Contains(input.Position))
        {
            return default;
        }

        UiPointerResult result = child.HandlePointer(input.ToLocal(bounds), bounds.Size);
        if (result.CapturePointer && input.Kind == UiPointerEventKind.Pressed)
        {
            Captured = child;
        }

        return result;
    }

    internal UiPointerResult DispatchCaptured(in UiPointerEvent input, RectangleF bounds)
    {
        IUiElement child = Captured!;
        // Clear before callbacks: release may execute a command that replaces content.
        if (input.Kind is UiPointerEventKind.Released or UiPointerEventKind.Cancelled)
        {
            Captured = null;
        }

        return child.HandlePointer(input.ToLocal(bounds), bounds.Size) with { Consumed = true };
    }

    internal UiPointerResult Cancel()
    {
        if (Captured is not IUiElement child)
        {
            return default;
        }

        Captured = null;
        return child.HandlePointer(
            new UiPointerEvent(UiPointerEventKind.Cancelled, PointF.Empty),
            SizeF.Empty);
    }
}
