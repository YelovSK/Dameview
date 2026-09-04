using System.Drawing;
using Dameview.UI.Animation;

namespace Dameview.UI.Components;

internal sealed class ButtonInteraction
{
    private readonly Action _clicked;
    private readonly AnimatedFloat _hoverAmount = new(0.0f, 24.0);
    private readonly AnimatedFloat _pressedAmount = new(0.0f, 32.0);
    private bool _isPressed;

    internal ButtonInteraction(Action clicked)
    {
        _clicked = clicked;
    }

    internal float HoverAmount => _hoverAmount.Current;
    internal float PressedAmount => _pressedAmount.Current;

    internal bool Update(double elapsedSeconds)
    {
        bool continues = _hoverAmount.Update(elapsedSeconds);
        return _pressedAmount.Update(elapsedSeconds) || continues;
    }

    internal UiPointerResult HandlePointer(in UiPointerEvent input, SizeF size)
    {
        bool isInside = new RectangleF(PointF.Empty, size).Contains(input.Position);

        switch (input.Kind)
        {
            case UiPointerEventKind.Moved:
                bool changed = _hoverAmount.SetTarget(isInside ? 1.0f : 0.0f);
                changed |= _pressedAmount.SetTarget(
                    _isPressed && isInside ? 1.0f : 0.0f);
                return new UiPointerResult(Consumed: isInside, NeedsRepaint: changed);

            case UiPointerEventKind.Pressed
                when input.Button == PointerButton.Primary && isInside:
                _isPressed = true;
                _hoverAmount.SetTarget(1.0f);
                _pressedAmount.SetTarget(1.0f);
                return new UiPointerResult(Consumed: true, NeedsRepaint: true, CapturePointer: true);

            case UiPointerEventKind.Released when _isPressed:
                _isPressed = false;
                _hoverAmount.SetTarget(isInside ? 1.0f : 0.0f);
                _pressedAmount.SetTarget(0.0f);

                if (isInside)
                {
                    _clicked();
                }

                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case UiPointerEventKind.Cancelled:
                bool wasPressed = _isPressed;
                _isPressed = false;
                bool repaint = _hoverAmount.SetTarget(0.0f);
                repaint |= _pressedAmount.SetTarget(0.0f);
                return new UiPointerResult(Consumed: wasPressed, NeedsRepaint: repaint);

            case UiPointerEventKind.DoubleClicked
                when input.Button == PointerButton.Primary && isInside:
                _clicked();
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case UiPointerEventKind.Wheel when isInside:
                return new UiPointerResult(Consumed: true);

            default:
                return default;
        }
    }

}
