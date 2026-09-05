using System.Drawing;
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

internal sealed class TextBlock : UiElement, IDisposable
{
    private readonly IDWriteTextFormat _format;
    private readonly float _lineHeight;
    private string _text;
    private UiTextTone _tone;

    internal TextBlock(
        IDWriteFactory factory,
        string text,
        UiTextStyle style,
        UiTextTone tone,
        UiTextWrapping wrapping,
        UiDesignTokens? design = null)
    {
        UiDesignTokens resolvedDesign = design ?? UiDesignTokens.Default;
        _text = text;
        _tone = tone;
        _lineHeight = style == UiTextStyle.Heading ? 36.0f : 24.0f;
        _format = factory.CreateTextFormat(
            "Segoe UI Variable",
            style == UiTextStyle.Heading ? FontWeight.SemiBold : FontWeight.Normal,
            FontStyle.Normal,
            style == UiTextStyle.Heading ? resolvedDesign.HeadingFontSize : resolvedDesign.BodyFontSize);
        _format.WordWrapping = wrapping == UiTextWrapping.Wrap
            ? WordWrapping.Wrap
            : WordWrapping.NoWrap;
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
            InvalidateVisual();
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
        return new SizeF(MathF.Max(0.0f, width), _lineHeight);
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        context.DrawText(
            Text,
            _format,
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

    public void Dispose() => _format.Dispose();
}
