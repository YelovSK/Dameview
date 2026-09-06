using System.Drawing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal sealed class Toggle : InteractiveControl, IDisposable
{
    private readonly Action<bool> _changed;
    private readonly IDWriteTextFormat _textFormat;
    private bool _value;

    internal Toggle(
        IDWriteFactory factory,
        string label,
        bool value,
        Action<bool> changed)
    {
        Label = label;
        _value = value;
        _changed = changed;
        _textFormat = factory.CreateTextFormat(
            UiTypography.FontFamily, FontWeight.Normal, FontStyle.Normal, UiDesign.BodyFontSize);
        _textFormat.ParagraphAlignment = ParagraphAlignment.Center;
        _textFormat.WordWrapping = WordWrapping.NoWrap;
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
            _textFormat,
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
        context.FillRoundedRectangle(
            track,
            Value ? context.Palette.Accent : context.Palette.SurfaceBorder,
            IsEnabled ? 1.0f : 0.55f);

        const float thumbSize = 16.0f;
        float thumbX = Value ? trackX + trackWidth - thumbSize - 3.0f : trackX + 3.0f;
        var thumb = new RoundedRectangle(
            new RectangleF(thumbX, trackY + 3.0f, thumbSize, thumbSize),
            thumbSize / 2.0f,
            thumbSize / 2.0f);
        context.FillRoundedRectangle(thumb, context.Palette.PrimaryText, IsEnabled ? 1.0f : 0.65f);
    }

    protected override void Activate()
    {
        Value = !Value;
        _changed(Value);
    }

    public void Dispose() => _textFormat.Dispose();
}
