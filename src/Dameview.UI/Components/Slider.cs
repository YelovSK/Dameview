using System.Drawing;
using Dameview.UI.Animation;
using Dameview.UI.Foundation;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

/// <summary>
/// Picks a number between a minimum and a maximum, snapped to whole steps from the minimum.
/// Dragging and clicking move it, and so do the arrow keys, one step at a time.
/// </summary>
internal sealed class Slider : InteractiveControl
{
    private const float BarHeight = 16.0f;
    private const float BarCornerRadius = 5.0f;
    private const float HandleWidth = 6.0f;
    private const float HandleHoveredWidth = 9.0f;
    private const float HandleGrabbedWidth = 12.0f;
    // How far a grabbed handle sticks out above and below the bar, as if lifted off it.
    private const float HandleGrabbedOverhang = 3.0f;
    private const double GrabResponse = 24.0;
    // Clear space between the handle and the bar on each side, so the handle reads as its own piece.
    private const float HandleGap = 3.0f;
    private const float ValueWidth = 56.0f;
    private const float ValueGap = 8.0f;
    private const double GlideResponse = 20.0;
    private static readonly UiFont ValueFont = new(UiDesign.BodyFontSize, Alignment: TextAlignment.Trailing);

    private readonly Action<double> _changed;
    private readonly Func<double, string>? _formatValue;
    // Where the thumb is drawn, as a share of the track. It glides after the value, so jumps
    // from a click, a key or a jerky drag still look smooth.
    private readonly AnimatedFloat _shownFraction;
    private readonly AnimatedFloat _grabAmount;
    private bool _dragging;
    // Touchpads scroll in small pieces of a notch, so they add up here until there's a whole step.
    private double _wheelNotches;

    /// <param name="formatValue">Turns the value into the text shown beside the track. Without it, no value is shown.</param>
    internal Slider(
        double minimum,
        double maximum,
        double step,
        double value,
        Action<double> changed,
        Func<double, string>? formatValue = null)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(minimum, maximum);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        _changed = changed;
        _formatValue = formatValue;
        _shownFraction = Animate(GetFraction(Snap(value)), GlideResponse);
        _grabAmount = Animate(0.0f, GrabResponse);
        Value = value;
    }

    private double Minimum { get; }
    private double Maximum { get; }
    private double Step { get; }

    /// <summary>Setting it snaps to a step but doesn't report a change, since the caller made it.</summary>
    internal double Value
    {
        get;
        set
        {
            field = Snap(value);
            _shownFraction.SetTarget(GetFraction(field));
            InvalidateVisual();
        }
    }

    internal override bool OnKeyEvent(WindowKeyEvent input)
    {
        if (!IsEnabled)
        {
            return false;
        }

        double? target = input.Key switch
        {
            WindowKey.Left => Value - Step,
            WindowKey.Right => Value + Step,
            WindowKey.Home => Minimum,
            WindowKey.End => Maximum,
            _ => null,
        };
        if (target is not { } value)
        {
            return base.OnKeyEvent(input);
        }

        Change(value);
        return true;
    }

    internal override UiPointerResult OnPointerEvent(in WindowPointerEvent input)
    {
        if (!IsEnabled)
        {
            return default;
        }

        switch (input.Kind)
        {
            case WindowPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                _dragging = true;
                _grabAmount.SetTarget(1.0f);
                ChangeAt(input.Position.X);
                return new UiPointerResult(Consumed: true, NeedsRepaint: true, CapturePointer: true);

            case WindowPointerEventKind.Moved when _dragging:
                ChangeAt(input.Position.X);
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case WindowPointerEventKind.Released or WindowPointerEventKind.Cancelled when _dragging:
                _dragging = false;
                _grabAmount.SetTarget(0.0f);
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case WindowPointerEventKind.Wheel:
                _wheelNotches += input.WheelDelta / 120.0;
                double steps = Math.Truncate(_wheelNotches);
                _wheelNotches -= steps;
                if (steps != 0.0)
                {
                    Change(Value + (steps * Step));
                }

                return new UiPointerResult(Consumed: true, NeedsRepaint: steps != 0.0);

            default:
                return default;
        }
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float width = float.IsFinite(availableSize.Width) ? availableSize.Width : 220.0f;
        return new SizeF(MathF.Max(0.0f, width), 36.0f);
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        (float trackLeft, float trackWidth) = GetTrack();
        float barRight = trackLeft + trackWidth + (HandleGrabbedWidth / 2.0f);
        float barTop = (Bounds.Height - BarHeight) / 2.0f;
        float handleX = trackLeft + (trackWidth * _shownFraction.Current);
        float opacity = IsEnabled ? 1.0f : 0.55f;

        // Widens a little under the pointer, and more once grabbed. A drag that leaves the control
        // keeps the hover width, since the handle is still in hand.
        float grab = _grabAmount.Current;
        float hover = MathF.Max(HoverAmount, grab);
        float handleWidth = HandleWidth
            + ((HandleHoveredWidth - HandleWidth) * hover)
            + ((HandleGrabbedWidth - HandleHoveredWidth) * grab);
        float handleOverhang = HandleGrabbedOverhang * grab;
        float handleLeft = handleX - (handleWidth / 2.0f);
        float handleRight = handleX + (handleWidth / 2.0f);

        FillBarPart(context, 0.0f, handleLeft - HandleGap, context.Palette.Accent, opacity);
        FillBarPart(context, handleRight + HandleGap, barRight, context.Palette.SurfaceBorder, opacity);
        context.FillRoundedRectangle(
            new RoundedRectangle(
                new RectangleF(handleLeft, barTop - handleOverhang, handleWidth, BarHeight + (2.0f * handleOverhang)),
                handleWidth / 2.0f,
                handleWidth / 2.0f),
            context.Palette.PrimaryText,
            opacity);

        if (_formatValue is { } format)
        {
            context.DrawText(
                format(Value),
                ValueFont,
                new Rect(Bounds.Width - ValueWidth, 0.0f, ValueWidth, Bounds.Height),
                context.Palette.SecondaryText,
                DrawTextOptions.Clip);
        }
    }

    private void FillBarPart(in UiDrawContext context, float left, float right, Color4 color, float opacity)
    {
        float width = right - left;
        if (width <= 0.0f)
        {
            return;
        }

        // A sliver next to the end of the bar can't take the full rounding.
        float radius = MathF.Min(BarCornerRadius, width / 2.0f);
        context.FillRoundedRectangle(
            new RoundedRectangle(
                new RectangleF(left, (Bounds.Height - BarHeight) / 2.0f, width, BarHeight),
                radius,
                radius),
            color,
            opacity);
    }

    // Only the arrow keys and the pointer change the value; Space and Enter have nothing to do.
    protected override void Activate()
    {
    }

    // The handle's middle travels the track, which stops half a handle short of each end of
    // the bar so the handle never sticks out.
    private (float Left, float Width) GetTrack()
    {
        float valueSpace = _formatValue is null ? 0.0f : ValueWidth + ValueGap;
        float left = HandleGrabbedWidth / 2.0f;
        return (left, MathF.Max(0.0f, Bounds.Width - valueSpace - HandleGrabbedWidth));
    }

    private float GetFraction(double value) => (float)((value - Minimum) / (Maximum - Minimum));

    private void ChangeAt(float x)
    {
        (float trackLeft, float trackWidth) = GetTrack();
        float fraction = trackWidth <= 0.0f ? 0.0f : Math.Clamp((x - trackLeft) / trackWidth, 0.0f, 1.0f);
        Change(Minimum + (fraction * (Maximum - Minimum)));
    }

    private void Change(double value)
    {
        double previous = Value;
        Value = value;
        if (Value != previous)
        {
            _changed(Value);
        }
    }

    private double Snap(double value)
    {
        double steps = Math.Round((value - Minimum) / Step);
        return Math.Clamp(Minimum + (steps * Step), Minimum, Maximum);
    }
}
