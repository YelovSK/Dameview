using System.Drawing;
using Dameview.UI.Foundation;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal enum UiTextStyle
{
    Body,
    Label,
    Heading,
    Title,
    Caption,
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
        UiTextWrapping wrapping,
        TextAlignment alignment = TextAlignment.Leading)
    {
        _text = text;
        _tone = tone;
        _wrapping = wrapping;
        (float fontSize, FontWeight weight, _lineHeight) = style switch
        {
            UiTextStyle.Body => (UiDesign.BodyFontSize, FontWeight.Normal, 24.0f),
            UiTextStyle.Label => (UiDesign.BodyFontSize, FontWeight.Medium, 24.0f),
            UiTextStyle.Heading => (UiDesign.HeadingFontSize, FontWeight.SemiBold, 36.0f),
            UiTextStyle.Title => (UiDesign.TitleFontSize, FontWeight.SemiBold, 44.0f),
            UiTextStyle.Caption => (UiDesign.CaptionFontSize, FontWeight.Medium, 16.0f),
            _ => throw new ArgumentOutOfRangeException(nameof(style)),
        };
        _font = new UiFont(
            fontSize,
            weight,
            alignment,
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
        float maxWidth = float.IsFinite(availableSize.Width) ? MathF.Max(0.0f, availableSize.Width) : 10_000.0f;
        if (maxWidth == 0.0f || Text.Length == 0)
        {
            return new SizeF(0.0f, _lineHeight);
        }

        TextMetrics metrics =TextLayouts.Get(Text, _font, new SizeF(maxWidth, 100_000.0f)).Metrics;
        return new SizeF(
            MathF.Min(maxWidth, MathF.Ceiling(metrics.WidthIncludingTrailingWhitespace)),
            _wrapping == UiTextWrapping.Wrap ? MathF.Max(_lineHeight, metrics.Height) : _lineHeight);
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

    // Text is only something to point at when it has a tooltip to show.
    protected override bool HitTestCore(PointF position) => ToolTip is not null;
}
