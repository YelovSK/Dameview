using System.Drawing;
using Dameview.UI.Foundation;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal enum UiButtonTone
{
    Default,
    Danger,
}

internal sealed class Button : InteractiveControl
{
    private readonly Action _clicked;
    private readonly float _backgroundInsetY;
    private readonly UiFont _font;
    private readonly UiButtonTone _tone;
    private string _label;

    internal Button(
        string label,
        Action clicked,
        string fontFamily = UiTypography.FontFamily,
        float fontSize = UiDesign.BodyFontSize,
        float backgroundInsetY = 0.0f,
        UiButtonTone tone = UiButtonTone.Default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(backgroundInsetY);
        _label = label;
        _clicked = clicked;
        _backgroundInsetY = backgroundInsetY;
        _tone = tone;
        _font = new UiFont(fontSize, FontWeight.SemiBold, TextAlignment.Center, Family: fontFamily);
    }

    internal string Label
    {
        get => _label;
        set
        {
            if (_label == value)
            {
                return;
            }

            _label = value;
            InvalidateVisual();
        }
    }
    internal bool IsSelected
    {
        get => HasVisualState(UiVisualState.Selected);
        set => SetVisualState(UiVisualState.Selected, value);
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float width = float.IsFinite(availableSize.Width) ? availableSize.Width : 96.0f;
        return new SizeF(MathF.Max(0.0f, width), 36.0f);
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        float width = Bounds.Width;
        float height = Bounds.Height;
        var bounds = new RectangleF(
            0.0f,
            _backgroundInsetY,
            width,
            MathF.Max(0.0f, height - 2.0f * _backgroundInsetY));
        var background = new RoundedRectangle(
            bounds,
            UiDesign.ControlCornerRadius,
            UiDesign.ControlCornerRadius);
        context.FillRoundedRectangle(background, context.Palette.ControlSurface);
        Color4 textColor = _tone switch
        {
            UiButtonTone.Default => context.Palette.PrimaryText,
            UiButtonTone.Danger => context.Palette.ErrorText,
            _ => throw new InvalidOperationException("Unknown button tone."),
        };

        if (IsSelected)
        {
            Color4 selectionColor = _tone == UiButtonTone.Danger
                ? context.Palette.ErrorText
                : context.Palette.Accent;
            context.FillRoundedRectangle(background, selectionColor, 0.22f);
        }

        if (HoverAmount > 0.0f)
        {
            context.FillRoundedRectangle(background, context.Palette.ControlHover, HoverAmount);
        }

        if (PressedAmount > 0.0f)
        {
            context.FillRoundedRectangle(background, context.Palette.ControlPressed, PressedAmount);
        }

        context.DrawText(
            Label,
            _font,
            new Rect(0.0f, 0.0f, width, height),
            IsEnabled ? textColor : context.Palette.SecondaryText,
            DrawTextOptions.Clip);
    }

    protected override void Activate() => _clicked();
}
