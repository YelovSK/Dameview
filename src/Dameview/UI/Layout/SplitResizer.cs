using System.Drawing;
using Dameview.UI.Foundation;
using Dameview.Win32.Input;
using Vortice.Direct2D1;

namespace Dameview.UI.Layout;

internal interface ISplitResizerTarget
{
    public UiOrientation Orientation { get; }
    public bool CanResize { get; }
    public float DividerPosition { get; set; }
}

internal sealed class SplitResizer(ISplitResizerTarget target) : UiElement
{
    private bool _dragging;
    private float _dragStartPointer;
    private float _dragStartPosition;

    internal override bool IsHitTestVisible => target.CanResize;
    internal override bool PreservesFocusOnPointerPress => true;
    internal override WindowCursor Cursor => target.Orientation == UiOrientation.Horizontal
        ? WindowCursor.ResizeHorizontal
        : WindowCursor.ResizeVertical;

    protected override void DrawCore(in UiDrawContext context)
    {
        if (!target.CanResize)
        {
            return;
        }

        bool highlighted = HasVisualState(UiVisualState.Hovered) || _dragging;
        float thickness = highlighted ? 3.0f : 2.0f;
        RectangleF bar = target.Orientation == UiOrientation.Horizontal
            ? new RectangleF(
                (Bounds.Width - thickness) / 2.0f,
                12.0f,
                thickness,
                MathF.Max(0.0f, Bounds.Height - 24.0f))
            : new RectangleF(
                12.0f,
                (Bounds.Height - thickness) / 2.0f,
                MathF.Max(0.0f, Bounds.Width - 24.0f),
                thickness);
        context.FillRoundedRectangle(
            new RoundedRectangle(bar, 1.0f, 1.0f),
            _dragging
                ? context.Palette.Accent
                : highlighted ? context.Palette.PrimaryText : context.Palette.SurfaceBorder,
            _dragging ? 1.0f : highlighted ? 0.9f : 0.65f);
    }

    internal override UiPointerResult OnPointerEvent(in WindowPointerEvent input)
    {
        switch (input.Kind)
        {
            case WindowPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                _dragging = true;
                _dragStartPointer = GetPointerCoordinate(input.Position);
                _dragStartPosition = target.DividerPosition;
                return new UiPointerResult(Consumed: true, CapturePointer: true, NeedsRepaint: true);

            case WindowPointerEventKind.Moved when _dragging:
                target.DividerPosition = _dragStartPosition + GetPointerCoordinate(input.Position) - _dragStartPointer;
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case WindowPointerEventKind.Released when _dragging:
                _dragging = false;
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case WindowPointerEventKind.Cancelled when _dragging:
                _dragging = false;
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            default:
                return default;
        }
    }

    private float GetPointerCoordinate(PointF localPosition)
    {
        return target.Orientation == UiOrientation.Horizontal
            ? Bounds.X + localPosition.X
            : Bounds.Y + localPosition.Y;
    }
}
