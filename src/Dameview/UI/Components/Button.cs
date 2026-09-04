using System.Drawing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal sealed class Button : IUiElement, IDisposable
{
    private readonly string _label;
    private readonly ID2D1SolidColorBrush _hoverBrush;
    private readonly ID2D1SolidColorBrush _pressedBrush;
    private readonly ID2D1SolidColorBrush _textBrush;
    private readonly IDWriteTextFormat _textFormat;
    private readonly ButtonInteraction _interaction;

    internal Button(
        ID2D1RenderTarget renderTarget,
        IDWriteFactory directWriteFactory,
        UiTheme theme,
        string label,
        Action clicked)
    {
        _label = label;
        _interaction = new ButtonInteraction(clicked);
        _hoverBrush = renderTarget.CreateSolidColorBrush(theme.ControlHover);
        _pressedBrush = renderTarget.CreateSolidColorBrush(theme.ControlPressed);
        _textBrush = renderTarget.CreateSolidColorBrush(theme.PrimaryText);
        _textFormat = directWriteFactory.CreateTextFormat(
            "Segoe UI Variable",
            FontWeight.SemiBold,
            FontStyle.Normal,
            14.0f);
        _textFormat.TextAlignment = TextAlignment.Center;
        _textFormat.ParagraphAlignment = ParagraphAlignment.Center;
        _textFormat.WordWrapping = WordWrapping.NoWrap;
    }

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

        _hoverBrush.Opacity = context.Opacity * _interaction.HoverAmount;
        _pressedBrush.Opacity = context.Opacity * _interaction.PressedAmount;
        _textBrush.Opacity = context.Opacity;

        if (_interaction.HoverAmount > 0.0f)
        {
            context.RenderTarget.FillRoundedRectangle(background, _hoverBrush);
        }

        if (_interaction.PressedAmount > 0.0f)
        {
            context.RenderTarget.FillRoundedRectangle(background, _pressedBrush);
        }

        context.RenderTarget.DrawText(
            _label,
            _textFormat,
            new Rect(0.0f, 0.0f, width, height),
            _textBrush,
            DrawTextOptions.Clip);
    }

    public UiPointerResult HandlePointer(in UiPointerEvent input, SizeF size)
    {
        return _interaction.HandlePointer(input, size);
    }

    public void Dispose()
    {
        _textFormat.Dispose();
        _textBrush.Dispose();
        _pressedBrush.Dispose();
        _hoverBrush.Dispose();
    }
}
