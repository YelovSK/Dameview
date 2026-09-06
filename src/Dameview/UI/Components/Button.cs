using System.Drawing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal sealed class Button : InteractiveControl, IDisposable
{
    private readonly Action _clicked;
    private readonly float _backgroundInsetY;
    private readonly IDWriteTextFormat _textFormat;
    private string _label;

    internal Button(
        IDWriteFactory directWriteFactory,
        string label,
        Action clicked,
        string fontFamily = UiTypography.FontFamily,
        float fontSize = UiDesign.BodyFontSize,
        float backgroundInsetY = 0.0f)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(backgroundInsetY);
        _label = label;
        _clicked = clicked;
        _backgroundInsetY = backgroundInsetY;
        _textFormat = directWriteFactory.CreateTextFormat(
            fontFamily,
            FontWeight.SemiBold,
            FontStyle.Normal,
            fontSize);
        _textFormat.TextAlignment = TextAlignment.Center;
        _textFormat.ParagraphAlignment = ParagraphAlignment.Center;
        _textFormat.WordWrapping = WordWrapping.NoWrap;
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

        if (IsSelected)
        {
            context.FillRoundedRectangle(background, context.Palette.Accent, 0.22f);
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
            _textFormat,
            new Rect(0.0f, 0.0f, width, height),
            IsEnabled ? context.Palette.PrimaryText : context.Palette.SecondaryText,
            DrawTextOptions.Clip);
    }

    public void Dispose()
    {
        _textFormat.Dispose();
    }

    protected override void Activate() => _clicked();
}
