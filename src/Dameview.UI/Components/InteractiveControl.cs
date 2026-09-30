using System.Drawing;
using Dameview.UI.Animation;
using Dameview.UI.Foundation;
using Dameview.Win32.Input;

namespace Dameview.UI.Components;

internal abstract class InteractiveControl : UiElement
{
    private readonly AnimatedFloat _hoverAmount;
    private readonly AnimatedFloat _pressedAmount;

    protected InteractiveControl()
    {
        _hoverAmount = Animate(0.0f, UiDesign.HoverResponse);
        _pressedAmount = Animate(0.0f, UiDesign.PressedResponse);
    }

    internal bool IsEnabled
    {
        get => !HasVisualState(UiVisualState.Disabled);
        set
        {
            if (IsEnabled == value)
            {
                return;
            }

            if (!value)
            {
                Root?.DisconnectSubtree(this);
            }

            SetVisualState(UiVisualState.Disabled, !value);
        }
    }

    internal override bool IsFocusable => IsEnabled;
    internal override WindowCursor Cursor => IsEnabled ? WindowCursor.Pointer : WindowCursor.Default;
    protected float HoverAmount => _hoverAmount.Current;
    protected float PressedAmount => _pressedAmount.Current;

    internal override UiPointerResult OnPointerEvent(in WindowPointerEvent input)
    {
        if (!IsEnabled)
        {
            return default;
        }

        bool isInside = new RectangleF(PointF.Empty, Bounds.Size).Contains(input.Position);
        switch (input.Kind)
        {
            case WindowPointerEventKind.Pressed
                when input.Button == PointerButton.Primary && isInside:
                // We react on mouse down instead of mouse up,
                // so that the UI feels more responsive.
                Activate();
                return new UiPointerResult(
                    Consumed: true,
                    NeedsRepaint: true,
                    CapturePointer: Root is not null);

            case WindowPointerEventKind.Released when HasVisualState(UiVisualState.Pressed):
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case WindowPointerEventKind.Cancelled when HasVisualState(UiVisualState.Pressed):
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case WindowPointerEventKind.DoubleClicked
                when input.Button == PointerButton.Primary && isInside:
                Activate();
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            default:
                return default;
        }
    }

    internal override bool OnKeyEvent(WindowKeyEvent input)
    {
        if (!IsEnabled || input.Key is not (WindowKey.Space or WindowKey.Enter))
        {
            return false;
        }

        Activate();
        return true;
    }

    protected override void OnVisualStateChanged()
    {
        _hoverAmount.SetTarget(IsEnabled && HasVisualState(UiVisualState.Hovered) ? 1.0f : 0.0f);
        _pressedAmount.SetTarget(
            IsEnabled
            && HasVisualState(UiVisualState.Pressed)
            && HasVisualState(UiVisualState.Hovered)
                ? 1.0f
                : 0.0f);
    }

    protected abstract void Activate();
}
