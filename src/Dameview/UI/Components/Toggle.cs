using System.Drawing;
using Dameview.UI.Animation;
using Dameview.UI.Foundation;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal sealed class Toggle : InteractiveControl
{
    private const double SwitchResponse = 24.0;
    private static readonly UiFont LabelFont = new(UiDesign.BodyFontSize);

    private readonly Action<bool> _changed;
    // 0 when off and 1 when on; slides the thumb and blends the track color in between.
    private readonly AnimatedFloat _onAmount;
    private bool _value;

    internal Toggle(
        string label,
        bool value,
        Action<bool> changed)
    {
        Label = label;
        _value = value;
        _onAmount = Animate(value ? 1.0f : 0.0f, SwitchResponse);
        _changed = changed;
        SetVisualState(UiVisualState.Selected, value);
    }

    internal string Label { get; set; }
    internal bool Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            _onAmount.SetTarget(value ? 1.0f : 0.0f);
            SetVisualState(UiVisualState.Selected, value);
        }
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float width = float.IsFinite(availableSize.Width) ? availableSize.Width : 220.0f;
        return new SizeF(MathF.Max(0.0f, width), 36.0f);
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        float width = Bounds.Width;
        float height = Bounds.Height;
        var controlBounds = new RoundedRectangle(
            new RectangleF(0.0f, 0.0f, width, height),
            UiDesign.ControlCornerRadius,
            UiDesign.ControlCornerRadius);
        context.FillRoundedRectangle(controlBounds, context.Palette.ControlSurface);
        if (HoverAmount > 0.0f)
        {
            context.FillRoundedRectangle(controlBounds, context.Palette.ControlHover, HoverAmount);
        }

        if (PressedAmount > 0.0f)
        {
            context.FillRoundedRectangle(controlBounds, context.Palette.ControlPressed, PressedAmount);
        }

        context.DrawText(
            Label,
            LabelFont,
            new Rect(12.0f, 0.0f, MathF.Max(12.0f, width - 56.0f), height),
            IsEnabled ? context.Palette.PrimaryText : context.Palette.SecondaryText,
            DrawTextOptions.Clip);

        const float trackWidth = 40.0f;
        const float trackHeight = 22.0f;
        float trackX = MathF.Max(4.0f, width - trackWidth - 8.0f);
        float trackY = (height - trackHeight) / 2.0f;
        var track = new RoundedRectangle(
            new RectangleF(trackX, trackY, trackWidth, trackHeight),
            trackHeight / 2.0f,
            trackHeight / 2.0f);
        float onAmount = _onAmount.Current;
        float trackOpacity = IsEnabled ? 1.0f : 0.55f;
        context.FillRoundedRectangle(track, context.Palette.SurfaceBorder, trackOpacity);
        if (onAmount > 0.0f)
        {
            context.FillRoundedRectangle(track, context.Palette.Accent, trackOpacity * onAmount);
        }

        const float thumbSize = 16.0f;
        const float thumbInset = 3.0f;
        float thumbTravel = trackWidth - thumbSize - (2.0f * thumbInset);
        float thumbX = trackX + thumbInset + (thumbTravel * onAmount);
        var thumb = new RoundedRectangle(
            new RectangleF(thumbX, trackY + thumbInset, thumbSize, thumbSize),
            thumbSize / 2.0f,
            thumbSize / 2.0f);
        context.FillRoundedRectangle(thumb, context.Palette.PrimaryText, IsEnabled ? 1.0f : 0.65f);
    }

    protected override void Activate()
    {
        Value = !Value;
        _changed(Value);
    }
}
