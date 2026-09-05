using System.Drawing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal sealed class Button : IUiElement, IDisposable
{
    private readonly Action _clicked;
    private readonly IDWriteTextFormat _textFormat;
    private readonly ButtonInteraction _interaction;

    internal Button(
        IDWriteFactory directWriteFactory,
        string label,
        Action clicked)
    {
        Label = label;
        _clicked = clicked;
        _interaction = new ButtonInteraction(clicked);
        _textFormat = directWriteFactory.CreateTextFormat(
            "Segoe UI Variable",
            FontWeight.SemiBold,
            FontStyle.Normal,
            14.0f);
        _textFormat.TextAlignment = TextAlignment.Center;
        _textFormat.ParagraphAlignment = ParagraphAlignment.Center;
        _textFormat.WordWrapping = WordWrapping.NoWrap;
    }

    internal string Label { get; set; }
    internal bool IsSelected { get; set; }
    internal bool IsFocused { get; set; }

    internal void Activate() => _clicked();

    public bool Update(in UiUpdateContext context)
    {
        return _interaction.Update(context.ElapsedSeconds);
    }

    public void Draw(in UiDrawContext context, SizeF size)
    {
        float width = context.PixelsToDips(size.Width);
        float height = context.PixelsToDips(size.Height);
        var bounds = new RectangleF(0.0f, 0.0f, width, height);
        var background = new RoundedRectangle(bounds, 8.0f, 8.0f);

        if (IsSelected)
        {
            context.FillRoundedRectangle(background, context.Palette.Accent, 0.22f);
        }

        if (_interaction.HoverAmount > 0.0f)
        {
            context.FillRoundedRectangle(background, context.Palette.ControlHover, _interaction.HoverAmount);
        }

        if (_interaction.PressedAmount > 0.0f)
        {
            context.FillRoundedRectangle(background, context.Palette.ControlPressed, _interaction.PressedAmount);
        }

        context.DrawText(
            Label,
            _textFormat,
            new Rect(0.0f, 0.0f, width, height),
            context.Palette.PrimaryText,
            DrawTextOptions.Clip);

        if (IsFocused)
        {
            context.DrawRoundedRectangle(new RoundedRectangle(
                new RectangleF(2, 2, MathF.Max(0, width - 4), MathF.Max(0, height - 4)),
                6, 6), context.Palette.Accent, 2);
        }
    }

    public UiPointerResult HandlePointer(in UiPointerEvent input, SizeF size)
    {
        return _interaction.HandlePointer(input, size);
    }

    public void Dispose()
    {
        _textFormat.Dispose();
    }
}
