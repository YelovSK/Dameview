using System.Drawing;
using Dameview.UI.Foundation;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal enum UiTextStyle
{
    Body,
    Heading,
}

internal enum UiTextTone
{
    Primary,
    Secondary,
    Error,
}

internal enum UiTextWrapping
{
    NoWrap,
    Wrap,
}

internal sealed class TextBlock : UiElement
{
    private readonly UiFont _font;
    private readonly float _lineHeight;
    private readonly UiTextWrapping _wrapping;
    private string _text;
    private UiTextTone _tone;

    internal TextBlock(
        string text,
        UiTextStyle style,
        UiTextTone tone,
        UiTextWrapping wrapping)
    {
        _text = text;
        _tone = tone;
        _wrapping = wrapping;
        _lineHeight = style == UiTextStyle.Heading ? 36.0f : 24.0f;
        _font = new UiFont(
            style == UiTextStyle.Heading ? UiDesign.HeadingFontSize : UiDesign.BodyFontSize,
            style == UiTextStyle.Heading ? FontWeight.SemiBold : FontWeight.Normal,
            VerticalAlignment: ParagraphAlignment.Near,
            Wrapping: wrapping == UiTextWrapping.Wrap ? WordWrapping.Wrap : WordWrapping.NoWrap);
    }

    internal string Text
    {
        get => _text;
        set
        {
            if (_text == value)
            {
                return;
            }

            _text = value;
            InvalidateLayout();
        }
    }

    internal UiTextTone Tone
    {
        get => _tone;
        set
        {
            if (_tone == value)
            {
                return;
            }

            _tone = value;
            InvalidateVisual();
        }
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float width = float.IsFinite(availableSize.Width) ? availableSize.Width : 0.0f;
        width = MathF.Max(0.0f, width);
        if (_wrapping == UiTextWrapping.NoWrap || width == 0.0f || Text.Length == 0)
        {
            return new SizeF(width, _lineHeight);
        }

        float height = TextLayouts.Get(Text, _font, new SizeF(width, 100_000.0f)).Metrics.Height;
        return new SizeF(width, MathF.Max(_lineHeight, height));
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        context.DrawText(
            Text,
            _font,
            new Rect(0.0f, 0.0f, Bounds.Width, Bounds.Height),
            Tone switch
            {
                UiTextTone.Primary => context.Palette.PrimaryText,
                UiTextTone.Secondary => context.Palette.SecondaryText,
                UiTextTone.Error => context.Palette.ErrorText,
                _ => throw new InvalidOperationException("Unknown text tone."),
            },
            DrawTextOptions.Clip);
    }

    protected override bool HitTestCore(PointF position) => false;
}
