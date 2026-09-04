using System.Drawing;
using Dameview.Commands;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class ToolbarPanel : IUiElement, IDisposable
{
    private const float Padding = 6.0f;
    private const float ButtonGap = 4.0f;

    private readonly ID2D1SolidColorBrush _backgroundBrush;
    private readonly ID2D1SolidColorBrush _borderBrush;
    private readonly ID2D1SolidColorBrush _hoverBrush;
    private readonly ID2D1SolidColorBrush _pressedBrush;
    private readonly ID2D1SolidColorBrush _textBrush;
    private readonly IDWriteTextFormat _textFormat;
    private readonly ToolbarButton[] _buttons;
    private float _dpi;
    private int _hotButton = -1;
    private int _pressedButton = -1;

    internal ToolbarPanel(
        ID2D1RenderTarget renderTarget,
        IDWriteFactory directWriteFactory,
        UiTheme theme,
        IViewerCommands commands,
        float dpi)
    {
        _dpi = dpi;
        _backgroundBrush = renderTarget.CreateSolidColorBrush(theme.OverlaySurface);
        _borderBrush = renderTarget.CreateSolidColorBrush(theme.SurfaceBorder);
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

        _buttons =
        [
            new ToolbarButton("←", commands.ShowPreviousImage),
            new ToolbarButton("→", commands.ShowNextImage),
            new ToolbarButton("Fit", commands.FitImage),
            new ToolbarButton("1:1", commands.ShowActualSize),
        ];
    }

    internal void SetDpi(float dpi)
    {
        _dpi = dpi;
    }

    public void Draw(in UiDrawContext context, SizeF size)
    {
        float width = context.PixelsToDips(size.Width);
        float height = context.PixelsToDips(size.Height);
        if (width <= 0.0f || height <= 0.0f)
        {
            return;
        }

        ID2D1RenderTarget renderTarget = context.RenderTarget;
        var panel = new RoundedRectangle(
            new RectangleF(0.0f, 0.0f, width, height),
            12.0f,
            12.0f);
        renderTarget.FillRoundedRectangle(panel, _backgroundBrush);
        renderTarget.DrawRoundedRectangle(panel, _borderBrush);

        for (int index = 0; index < _buttons.Length; index++)
        {
            RectangleF bounds = GetButtonBounds(index, width, height);
            var button = new RoundedRectangle(bounds, 8.0f, 8.0f);

            if (_pressedButton == index && _hotButton == index)
            {
                renderTarget.FillRoundedRectangle(button, _pressedBrush);
            }
            else if (_hotButton == index)
            {
                renderTarget.FillRoundedRectangle(button, _hoverBrush);
            }

            renderTarget.DrawText(
                _buttons[index].Label,
                _textFormat,
                new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                _textBrush,
                DrawTextOptions.Clip);
        }
    }

    public bool HandlePointer(in UiPointerEvent input, SizeF size)
    {
        int hitButton = HitTest(input.Position, size);

        switch (input.Kind)
        {
            case UiPointerEventKind.Moved:
                bool changed = _hotButton != hitButton;
                _hotButton = hitButton;
                return changed;

            case UiPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                _hotButton = hitButton;
                _pressedButton = hitButton;
                return true;

            case UiPointerEventKind.Released:
                int pressedButton = _pressedButton;
                _pressedButton = -1;
                _hotButton = hitButton;

                if (pressedButton >= 0 && pressedButton == hitButton)
                {
                    _buttons[pressedButton].Execute();
                }

                return true;

            case UiPointerEventKind.DoubleClicked when input.Button == PointerButton.Primary:
                if (hitButton >= 0)
                {
                    _buttons[hitButton].Execute();
                }

                return true;

            case UiPointerEventKind.Wheel:
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
        _borderBrush.Dispose();
        _backgroundBrush.Dispose();
    }

    private int HitTest(PointF position, SizeF size)
    {
        float scale = 96.0f / _dpi;
        var positionInDips = new PointF(position.X * scale, position.Y * scale);
        float width = size.Width * scale;
        float height = size.Height * scale;

        for (int index = 0; index < _buttons.Length; index++)
        {
            if (GetButtonBounds(index, width, height).Contains(positionInDips))
            {
                return index;
            }
        }

        return -1;
    }

    private RectangleF GetButtonBounds(int index, float width, float height)
    {
        float totalGaps = ButtonGap * (_buttons.Length - 1);
        float buttonWidth = MathF.Max(
            0.0f,
            (width - (2.0f * Padding) - totalGaps) / _buttons.Length);
        return new RectangleF(
            Padding + (index * (buttonWidth + ButtonGap)),
            Padding,
            buttonWidth,
            MathF.Max(0.0f, height - (2.0f * Padding)));
    }

    private readonly record struct ToolbarButton(string Label, Action Execute);
}
