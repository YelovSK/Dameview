using System.Drawing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal sealed class Button : IUiElement, IDisposable
{
    private readonly string _label;
    private readonly Action _clicked;
    private readonly ID2D1SolidColorBrush _hoverBrush;
    private readonly ID2D1SolidColorBrush _pressedBrush;
    private readonly ID2D1SolidColorBrush _textBrush;
    private readonly IDWriteTextFormat _textFormat;
    private bool _isHovered;
    private bool _isPressed;

    internal Button(
        ID2D1RenderTarget renderTarget,
        IDWriteFactory directWriteFactory,
        UiTheme theme,
        string label,
        Action clicked)
    {
        _label = label;
        _clicked = clicked;
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

    public void Draw(in UiDrawContext context, SizeF size)
    {
        float width = context.PixelsToDips(size.Width);
        float height = context.PixelsToDips(size.Height);
        var bounds = new RectangleF(0.0f, 0.0f, width, height);
        var background = new RoundedRectangle(bounds, 8.0f, 8.0f);

        if (_isPressed && _isHovered)
        {
            context.RenderTarget.FillRoundedRectangle(background, _pressedBrush);
        }
        else if (_isHovered)
        {
            context.RenderTarget.FillRoundedRectangle(background, _hoverBrush);
        }

        context.RenderTarget.DrawText(
            _label,
            _textFormat,
            new Rect(0.0f, 0.0f, width, height),
            _textBrush,
            DrawTextOptions.Clip);
    }

    public bool HandlePointer(in UiPointerEvent input, SizeF size)
    {
        bool isInside = new RectangleF(PointF.Empty, size).Contains(input.Position);

        switch (input.Kind)
        {
            case UiPointerEventKind.Moved:
                bool changed = _isHovered != isInside;
                _isHovered = isInside;
                return changed;

            case UiPointerEventKind.Pressed
                when input.Button == PointerButton.Primary && isInside:
                _isHovered = true;
                _isPressed = true;
                return true;

            case UiPointerEventKind.Released when _isPressed:
                _isPressed = false;
                _isHovered = isInside;

                if (isInside)
                {
                    _clicked();
                }

                return true;

            case UiPointerEventKind.DoubleClicked
                when input.Button == PointerButton.Primary && isInside:
                _clicked();
                return true;

            case UiPointerEventKind.Wheel when isInside:
                return true;

            default:
                return false;
        }
    }

    public void Dispose()
    {
        _textFormat.Dispose();
        _textBrush.Dispose();
        _pressedBrush.Dispose();
        _hoverBrush.Dispose();
    }
}
