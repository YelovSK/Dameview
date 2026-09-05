using System.Drawing;
using Dameview.Platform;
using Dameview.UI.Animation;

namespace Dameview.UI.Components;

internal abstract class InteractiveControl : UiElement
{
    private readonly AnimatedFloat _hoverAmount;
    private readonly AnimatedFloat _pressedAmount;

    protected InteractiveControl(UiDesignTokens design)
    {
        _hoverAmount = new AnimatedFloat(0.0f, design.HoverResponse);
        _pressedAmount = new AnimatedFloat(0.0f, design.PressedResponse);
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
    protected float HoverAmount => _hoverAmount.Current;
    protected float PressedAmount => _pressedAmount.Current;

    internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
    {
        if (!IsEnabled)
        {
            return default;
        }

        bool isInside = new RectangleF(PointF.Empty, Bounds.Size).Contains(input.Position);
        switch (input.Kind)
        {
            case UiPointerEventKind.Pressed
                when input.Button == PointerButton.Primary && isInside:
                return new UiPointerResult(Consumed: true, NeedsRepaint: true, CapturePointer: true);

            case UiPointerEventKind.Released when HasVisualState(UiVisualState.Pressed):
                if (isInside)
                {
                    Activate();
                }

                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case UiPointerEventKind.Cancelled when HasVisualState(UiVisualState.Pressed):
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case UiPointerEventKind.DoubleClicked
                when input.Button == PointerButton.Primary && isInside:
                Activate();
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            default:
                return default;
        }
    }

    internal override bool OnKeyEvent(UiKeyEvent input)
    {
        if (!IsEnabled || input.Key is not (UiKey.Space or UiKey.Enter))
        {
            return false;
        }

        Activate();
        return true;
    }

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        bool continues = _hoverAmount.Update(context.ElapsedSeconds);
        return _pressedAmount.Update(context.ElapsedSeconds) || continues;
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
